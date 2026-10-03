'use strict';
// Mono metadata gives us method/field identities; no managed address is guessed.
const profile = globalThis.QPRO_PROFILE.monoLayout;
const mono = Process.getModuleByName('libmonosgen-2.0.so');
const api = (name, result, args) => new NativeFunction(mono.getExportByName(name), result, args);
const rootDomain = api('mono_get_root_domain', 'pointer', [])();
const attachThread = api('mono_thread_attach', 'pointer', ['pointer']);
const detachThread = api('mono_thread_detach', 'void', ['pointer']);
const findAssembly = api('mono_assembly_loaded', 'pointer', ['pointer']);
const assemblyName = api('mono_assembly_name_new', 'pointer', ['pointer']);
const freeName = api('mono_assembly_name_free', 'void', ['pointer']);
const imageOf = api('mono_assembly_get_image', 'pointer', ['pointer']);
const findClass = api('mono_class_from_name', 'pointer', ['pointer', 'pointer', 'pointer']);
const methodOf = api('mono_class_get_method_from_name', 'pointer', ['pointer', 'pointer', 'int']);
const compile = api('mono_compile_method', 'pointer', ['pointer']);
const fieldOf = api('mono_class_get_field_from_name', 'pointer', ['pointer', 'pointer']);
const offsetOf = api('mono_field_get_offset', 'uint32', ['pointer']);
const parentOf = api('mono_class_get_parent', 'pointer', ['pointer']);
const className = api('mono_class_get_name', 'pointer', ['pointer']);
const vtableOf = api('mono_class_vtable', 'pointer', ['pointer', 'pointer']);
const staticValue = api('mono_field_static_get_value', 'void', ['pointer', 'pointer', 'pointer']);
const invoke = api('mono_runtime_invoke', 'pointer', ['pointer', 'pointer', 'pointer', 'pointer']);
const pinObject = api('mono_gchandle_new', 'uint32', ['pointer', 'int']);
const freePin = api('mono_gchandle_free', 'void', ['uint32']);
let state = 'idle', error = null, hooks = [], pins = [], lease = null;
let restoreTimer = null, hmd = null, originals = null, frames = 0;
const validFrames = [0, 0];
let lastValid = [0, 0];

function requirePointer(pointer, message) {
    if (pointer.isNull()) throw new Error(message);
    return pointer;
}
function managed(action) {
    const thread = attachThread(rootDomain);
    try { return action(); } finally { detachThread(thread); }
}
function classFor(assembly, namespace, name) {
    const descriptor = assemblyName(Memory.allocUtf8String(assembly));
    try {
        const loaded = requirePointer(findAssembly(descriptor), 'Managed assembly is not loaded: ' + assembly);
        return requirePointer(findClass(imageOf(loaded), Memory.allocUtf8String(namespace), Memory.allocUtf8String(name)), 'Managed class is missing: ' + name);
    } finally { freeName(descriptor); }
}
function method(klass, name, argumentsCount) {
    return requirePointer(methodOf(klass, Memory.allocUtf8String(name), argumentsCount), 'Managed method is missing: ' + name);
}
function field(klass, name) {
    return requirePointer(fieldOf(klass, Memory.allocUtf8String(name)), 'Managed field is missing: ' + name);
}
let bindings = null;
function resolve() {
    if (bindings !== null) return bindings;
    return managed(() => {
        const klass = classFor(profile.assembly, profile.namespace, profile.class);
        const settings = classFor(profile.settingsAssembly, profile.settingsNamespace, profile.settingsClass);
        let base = settings;
        for (let count = 0; count < 12; count++) {
            if (className(base).readUtf8String() === 'SettingsBase`1') break;
            base = requirePointer(parentOf(base), 'Settings singleton base is missing');
        }
        if (className(base).readUtf8String() !== 'SettingsBase`1') throw new Error('Settings inheritance is unsupported');
        const sharedSlot = Memory.alloc(Process.pointerSize);
        staticValue(vtableOf(rootDomain, base), field(base, '<Default>k__BackingField'), sharedSlot);
        const shared = requirePointer(sharedSlot.readPointer(), 'Settings singleton is unavailable');
        const get = name => requirePointer(compile(method(klass, name, name === 'ConvertFingerState' ? 2 : 1)), 'Managed method did not compile');
        bindings = {
            update: get('Update'), handState: get('GetHandState'),
            convert: new NativeFunction(get('ConvertFingerState'), 'int', ['pointer', 'int', 'pointer']),
            hmdSetter: method(klass, 'set_UseMultiModalInput', 1),
            sharedSetter: method(settings, 'set_UseMultiModal', 1), shared,
            hmdOffset: offsetOf(field(klass, '_useMultiModalInput')),
            sharedOffset: offsetOf(field(settings, '_useMultiModal'))
        };
        if (bindings.hmdOffset < Process.pointerSize * 2 || bindings.sharedOffset < Process.pointerSize * 2)
            throw new Error('Managed field offsets are unsupported');
        return bindings;
    });
}
function setBoolean(setter, object, value) {
    const argument = Memory.alloc(4); argument.writeS32(value);
    const parameters = Memory.alloc(Process.pointerSize); parameters.writePointer(argument);
    const exception = Memory.alloc(Process.pointerSize); exception.writePointer(ptr(0));
    invoke(setter, object, parameters, exception);
    if (!exception.readPointer().isNull()) throw new Error('Managed multimodal setter raised an exception');
}
function release() {
    for (const hook of hooks.splice(0)) hook.detach();
    for (const handle of pins.splice(0)) freePin(handle);
    if (lease !== null) { clearTimeout(lease); lease = null; }
    if (restoreTimer !== null) { clearTimeout(restoreTimer); restoreTimer = null; }
}
function restore() {
    if (originals === null) { release(); state = 'stopped'; return; }
    try {
        setBoolean(bindings.hmdSetter, hmd, originals.hmd);
        setBoolean(bindings.sharedSetter, bindings.shared, originals.shared);
        if (hmd.add(bindings.hmdOffset).readU8() !== originals.hmd ||
            bindings.shared.add(bindings.sharedOffset).readU8() !== originals.shared)
            throw new Error('Managed settings restoration did not verify');
        originals = null;
        release(); state = 'stopped';
    } catch (failure) { error = String(failure); state = 'restore-failed'; }
}
function deactivate() {
    if (state === 'stopped') return;
    if (state === 'idle') { state = 'stopped'; return; }
    state = 'restoring';
    // Prefer the existing VD Update thread. A stalled update loop uses a Mono-
    // attached fallback so app-owned fields do not remain changed after a drop.
    if (restoreTimer === null) restoreTimer = setTimeout(() => managed(restore), 2500);
}
function heartbeat(seconds) {
    if (!Number.isFinite(seconds) || seconds < 1 || seconds > 30 || !['starting', 'running'].includes(state))
        throw new Error('Invalid optical hand lease');
    if (lease !== null) clearTimeout(lease);
    lease = setTimeout(deactivate, seconds * 1000);
}
rpc.exports = {
    validate() { resolve(); return {compatible: true, managedMetadata: true}; },
    activate(seconds) {
        if (state !== 'idle') throw new Error('Optical hand adapter is already used');
        resolve(); state = 'starting';
        pins.push(managed(() => pinObject(bindings.shared, 1)));
        try {
            hooks.push(Interceptor.attach(bindings.update, {onEnter(args) {
                if (state === 'restoring') { restore(); return; }
                if (state !== 'starting') return;
                try {
                    hmd = args[0]; pins.push(pinObject(hmd, 1));
                    originals = {hmd: hmd.add(bindings.hmdOffset).readU8(), shared: bindings.shared.add(bindings.sharedOffset).readU8()};
                    setBoolean(bindings.sharedSetter, bindings.shared, 1);
                    setBoolean(bindings.hmdSetter, hmd, 1);
                    if (hmd.add(bindings.hmdOffset).readU8() !== 1 || bindings.shared.add(bindings.sharedOffset).readU8() !== 1)
                        throw new Error('Managed multimodal activation did not verify');
                    state = 'running';
                } catch (failure) { error = String(failure); deactivate(); }
            }}));
            hooks.push(Interceptor.attach(bindings.handState, {
                onEnter(args) { this.owner = args[0]; this.result = args[1]; },
                onLeave() {
                    if (state !== 'running') return;
                    try {
                        const locations = [profile.leftFingerOffset, profile.rightFingerOffset];
                        for (let side = 0; side < 2; side++) {
                            const valid = bindings.convert(this.owner, side, this.result.add(locations[side])) !== 0;
                            this.result.add(side).writeU8(valid ? 1 : 0);
                            if (valid) { validFrames[side]++; lastValid[side] = Date.now(); }
                        }
                        frames++;
                    } catch (failure) { error = String(failure); deactivate(); }
                }
            }));
            heartbeat(seconds);
        } catch (failure) { error = String(failure); deactivate(); throw failure; }
    },
    heartbeat, deactivate,
    status() { return {state, error, frames, validFrames, freshSides: lastValid.map(time => Date.now() - time < 500)}; },
    dispose() { deactivate(); return new Promise((accept, reject) => {
        const timer = setInterval(() => {
            if (state === 'stopped') { clearInterval(timer); accept(); }
            if (state === 'restore-failed') { clearInterval(timer); reject(new Error(error)); }
        }, 50);
    }); }
};
