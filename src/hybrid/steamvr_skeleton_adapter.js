'use strict';
// The host admits this adapter only for the exact driver fingerprint in the
// compatibility profile. Every live object and public interface is rechecked.
const configuration = globalThis.QPRO_PROFILE;
const layout = configuration.pcLayout;
let state = 'idle', error = null, driver = null, input = null, updateAddress = null;
let originalUpdate = null, replaced = false, poseHook = null, observer = null;
let poll = null, lease = null, interfaceVersion = null;
const sides = [null, null], submissions = [0, 0], lastSubmission = [0, 0];
const activeSides = [false, false];
let restored = 0;

function readable(address, bytes) {
    if (address.isNull()) return false;
    const range = Process.findRangeByAddress(address);
    return range !== null && range.protection.includes('r') && address.add(bytes).compare(range.base.add(range.size)) <= 0;
}
function writable(address, bytes) {
    const range = Process.findRangeByAddress(address);
    return readable(address, bytes) && range.protection.includes('w');
}
function executable(address) {
    const range = Process.findRangeByAddress(address);
    return range !== null && range.protection.includes('x');
}
function requireObject(address, bytes, description) {
    if (!readable(address, bytes)) throw new Error(description + ' is not readable');
    return address;
}
function sameObjects(record) {
    if (driver === null || Process.findModuleByName('driver_VirtualDesktop.dll')?.base.toString() !== driver.base.toString()) return false;
    const controller = driver.base.add(layout.controllerTable + record.side * Process.pointerSize).readPointer();
    return controller.equals(record.controller) && readable(controller, layout.handPointer + Process.pointerSize) &&
        controller.add(layout.handPointer).readPointer().equals(record.hand);
}
function skeletonHandle(controller, side) {
    const saved = driver.base.add(layout.skeletonTable + side * 8).readU64();
    return saved.equals(0) ? controller.add(layout.skeleton).readU64() : saved;
}
function resolveSide(side) {
    const controller = driver.base.add(layout.controllerTable + side * Process.pointerSize).readPointer();
    if (controller.isNull()) return null;
    requireObject(controller, layout.skeleton + 8, 'Controller');
    const hand = controller.add(layout.handPointer).readPointer();
    if (hand.isNull()) return null;
    requireObject(hand, layout.skeleton + 8, 'Hand');
    if (controller.add(layout.role).readU32() !== side + 1 || hand.add(layout.role).readU32() !== side + 1)
        throw new Error('The hand/controller role does not match the compatibility profile');
    for (const object of [controller, hand]) {
        if (!writable(object.add(layout.multiModal), 1) || !writable(object.add(layout.skeleton), 8))
            throw new Error('Hand routing fields are not writable');
        if (object.add(layout.multiModal).readU8() > 1) throw new Error('The multimodal field has an unexpected value');
    }
    const device = controller.add(layout.deviceIndex).readU32();
    const handDevice = hand.add(layout.deviceIndex).readU32();
    if (device >= 64 || handDevice >= 64 || device === handDevice) throw new Error('The SteamVR device identities are invalid');
    return {side, controller, hand, device, handDevice, routeActive: false,
        original: {controllerMode: controller.add(layout.multiModal).readU8(),
            handMode: hand.add(layout.multiModal).readU8(), handle: hand.add(layout.skeleton).readU64()},
        handle: skeletonHandle(controller, side), physical: false};
}
function validateBones(bones, count) {
    if (count !== configuration.boneCount || !readable(bones, count * 32)) return false;
    for (let index = 0; index < count * 8; index++) {
        if (!Number.isFinite(bones.add(index * 4).readFloat())) return false;
    }
    return true;
}
function restore(record) {
    if (record === null || !sameObjects(record)) return;
    record.hand.add(layout.skeleton).writeU64(record.original.handle);
    record.hand.add(layout.multiModal).writeU8(record.original.handMode);
    record.controller.add(layout.multiModal).writeU8(record.original.controllerMode);
    if (!record.hand.add(layout.skeleton).readU64().equals(record.original.handle) ||
        record.hand.add(layout.multiModal).readU8() !== record.original.handMode ||
        record.controller.add(layout.multiModal).readU8() !== record.original.controllerMode)
        throw new Error('SteamVR hand routing restoration did not verify');
    restored++; record.routeActive = false;
}
function deactivate() {
    if (state === 'stopped') return;
    state = 'restoring';
    const failures = [];
    if (poll !== null) { clearInterval(poll); poll = null; }
    if (lease !== null) { clearTimeout(lease); lease = null; }
    if (replaced) {
        try { Interceptor.revert(updateAddress); replaced = false; } catch (failure) { failures.push(String(failure)); }
    }
    if (poseHook !== null) {
        try { poseHook.detach(); poseHook = null; } catch (failure) { failures.push(String(failure)); }
    }
    if (observer !== null) { observer.detach(); observer = null; }
    for (const record of sides) {
        try { restore(record); } catch (failure) { failures.push(String(failure)); }
    }
    activeSides.fill(false);
    if (failures.length) { error = failures.join('; '); state = 'restore-failed'; }
    else state = 'stopped';
}
function fail(failure) { error = String(failure); deactivate(); }
function heartbeat(seconds) {
    if (!Number.isFinite(seconds) || seconds < 1 || seconds > 30 || state !== 'running') throw new Error('Invalid skeleton lease');
    if (lease !== null) clearTimeout(lease);
    lease = setTimeout(deactivate, seconds * 1000);
}
function resolveInterfaces() {
    driver = Process.getModuleByName('driver_VirtualDesktop.dll');
    if (driver.size <= Math.max(layout.skeletonTable, layout.controllerTable) + 16) throw new Error('The VD driver is too small for this profile');
    const context = requireObject(driver.base.add(layout.driverContext).readPointer(), Process.pointerSize, 'OpenVR driver context');
    const contextTable = requireObject(context.readPointer(), Process.pointerSize, 'OpenVR context table');
    const getAddress = contextTable.readPointer();
    if (!executable(getAddress)) throw new Error('OpenVR interface lookup is not executable');
    const getInterface = new NativeFunction(getAddress, 'pointer', ['pointer', 'pointer', 'pointer']);
    for (const name of configuration.interfaceVersions) {
        const result = Memory.alloc(4); result.writeS32(-1);
        const candidate = getInterface(context, Memory.allocUtf8String(name), result);
        if (candidate.isNull()) continue;
        requireObject(candidate, Process.pointerSize, 'OpenVR input interface');
        const table = requireObject(candidate.readPointer(), 7 * Process.pointerSize, 'OpenVR input vtable');
        const address = table.add(6 * Process.pointerSize).readPointer();
        if (!executable(address)) throw new Error('Skeleton update interface is invalid');
        input = candidate; updateAddress = address; interfaceVersion = name; break;
    }
    if (input === null) throw new Error('A supported OpenVR skeletal input interface is unavailable');
    const host = requireObject(driver.base.add(layout.driverHost).readPointer(), Process.pointerSize, 'OpenVR driver host');
    const hostTable = requireObject(host.readPointer(), 2 * Process.pointerSize, 'OpenVR host vtable');
    const poseAddress = hostTable.add(Process.pointerSize).readPointer();
    if (!executable(poseAddress)) throw new Error('TrackedDevicePoseUpdated is invalid');
    return poseAddress;
}
function updateRoutes() {
    if (state !== 'running') return;
    try {
        for (let side = 0; side < 2; side++) {
            let record = sides[side];
            if (record === null || !sameObjects(record)) record = sides[side] = resolveSide(side);
            if (record === null) { activeSides[side] = false; continue; }
            const data = record.controller.add(layout.dataPointer).readPointer();
            if (data.isNull()) { restore(record); record.physical = false; activeSides[side] = false; continue; }
            requireObject(data, layout.opticalFlags + 2, 'VD controller frame');
            const frame = data.readPointer();
            if (frame.isNull()) { restore(record); record.physical = false; activeSides[side] = false; continue; }
            requireObject(frame, layout.frameFlags + layout.frameSideStride * side + 1, 'VD tracking frame');
            const physical = (frame.add(layout.frameFlags + layout.frameSideStride * side).readU8() & 3) === 1;
            const optical = data.add(layout.opticalFlags + side).readU8();
            if (optical > 1) throw new Error('VD optical validity field is unsupported');
            const handle = skeletonHandle(record.controller, side);
            const enabled = physical && optical === 1 && !handle.equals(0);
            record.hand.add(layout.skeleton).writeU64(enabled ? handle : record.original.handle);
            record.hand.add(layout.multiModal).writeU8(physical ? 1 : record.original.handMode);
            record.controller.add(layout.multiModal).writeU8(enabled ? 1 : record.original.controllerMode);
            record.handle = handle; record.routeActive = enabled; record.physical = physical;
            activeSides[side] = enabled && Date.now() - lastSubmission[side] < 500;
        }
    } catch (failure) { fail(failure); }
}
rpc.exports = {
    validate() {
        resolveInterfaces(); sides[0] = resolveSide(0); sides[1] = resolveSide(1);
        return {compatible: true, interfaceVersion, boneCount: configuration.boneCount};
    },
    activate(seconds) {
        if (state !== 'idle') throw new Error('Skeleton adapter is already used');
        const poseAddress = resolveInterfaces(); state = 'running';
        try {
            originalUpdate = new NativeFunction(updateAddress, 'int', ['pointer', 'uint64', 'int', 'pointer', 'uint32']);
            Interceptor.replace(updateAddress, new NativeCallback((owner, handle, range, bones, count) => {
                let record = null;
                try {
                    if (state === 'running') record = sides.find(candidate => candidate !== null && sameObjects(candidate) && candidate.handle.equals(handle));
                    if (record?.routeActive && count === configuration.boneCount && !owner.equals(input)) return 0;
                    const result = originalUpdate(owner, handle, range, bones, count);
                    if (result === 0 && state === 'running' && record?.routeActive && validateBones(bones, count)) {
                        submissions[record.side]++; lastSubmission[record.side] = Date.now();
                    }
                    return result;
                } catch (failure) { fail(failure); return originalUpdate(owner, handle, range, bones, count); }
            }, 'int', ['pointer', 'uint64', 'int', 'pointer', 'uint32']));
            replaced = true;
            poseHook = Interceptor.attach(poseAddress, {onEnter(args) {
                if (state !== 'running') return;
                try {
                    const device = args[1].toUInt32();
                    const record = sides.find(candidate => candidate !== null && sameObjects(candidate) && candidate.handDevice === device);
                    if (record === undefined || !record.physical) return;
                    // Public DriverPose_t validity bytes for the admitted win64 ABI.
                    const pose = args[2];
                    if (args[3].toUInt32() < 280 || !writable(pose, 280)) throw new Error('DriverPose_t ABI mismatch');
                    pose.add(276).writeU8(0); pose.add(279).writeU8(0);
                } catch (failure) { fail(failure); }
            }});
            observer = Process.attachModuleObserver({onRemoved(module) {
                if (module.base.equals(driver.base)) fail('VD driver unloaded');
            }});
            poll = setInterval(updateRoutes, 16);
            heartbeat(seconds);
        } catch (failure) { fail(failure); throw failure; }
    },
    heartbeat, deactivate,
    status() { return {state, error, interfaceVersion, activeSides, skeletonSubmissions: submissions, restored}; },
    dispose: deactivate
};
