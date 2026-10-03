'use strict';
// Source syntax and selected lease/resolver guards are checked in an isolated
// VM. No real Frida APIs, OS processes or hardware are accessed.
const fs = require('fs');
const vm = require('vm');
const path = require('path');
const assert = require('assert');
const directory = path.resolve(__dirname, '..');
const profile = JSON.parse(fs.readFileSync(path.join(directory, 'compatibility.json')));
for (const name of ['quest_hand_adapter.js', 'quest_controller_adapter.js', 'steamvr_skeleton_adapter.js']) {
    new vm.Script(fs.readFileSync(path.join(directory, name), 'utf8'), {filename: name});
}
let attachments = 0, timers = [], detached = 0;
const context = vm.createContext({
    QPRO_PROFILE: profile, rpc: {exports: {}},
    Process: {arch: 'arm64', getModuleByName: () => ({enumerateRanges: () => []})},
    Interceptor: {attach: () => {attachments++; return {detach() {detached++;}};}},
    setTimeout: callback => {timers.push(callback); return timers.length;}, clearTimeout() {},
    ptr: value => value
});
vm.runInContext(fs.readFileSync(path.join(directory, 'quest_controller_adapter.js'), 'utf8'), context);
assert.throws(() => context.rpc.exports.validate(), /ambiguous or unsupported/);
assert.equal(attachments, 0);
assert.throws(() => context.rpc.exports.heartbeat(0), /Invalid controller lease/);
vm.runInContext('resolution = {callee: 123, caller: 456};', context);
context.rpc.exports.activate(20);
assert.equal(context.rpc.exports.status().state, 'running');
assert.equal(attachments, 1);
timers.at(-1)();
assert.equal(context.rpc.exports.status().state, 'stopped');
assert.equal(detached, 1);
console.log('Hybrid adapters: syntax, unsupported resolver, no preflight hook, lease expiry passed.');

class Address {
    constructor(value, memory = new Map()) { this.value = Number(value); this.memory = memory; }
    add(amount) { return new Address(this.value + amount, this.memory); }
    sub(amount) { return new Address(this.value - Number(amount instanceof Address ? amount.value : amount), this.memory); }
    compare(other) { return Math.sign(this.value - other.value); }
    equals(other) { return this.value === Number(other instanceof Address ? other.value : other); }
    isNull() { return this.value === 0; }
    toUInt32() { return this.value >>> 0; }
    toInt32() { return this.value | 0; }
    toString() { return '0x' + this.value.toString(16); }
    readPointer() { return new Address(this.memory.get(this.value) || 0, this.memory); }
    writePointer(value) { this.memory.set(this.value, value.value); }
    readU8() { return Number(this.memory.get(this.value) || 0); }
    readU32() { return Number(this.memory.get(this.value) || 0); }
    readFloat() { return this.memory.has(this.value) ? Number(this.memory.get(this.value)) : 0; }
    readU64() { return new Handle(this.memory.get(this.value) || 0); }
    writeU8(value) { this.memory.set(this.value, value); }
    writeS32(value) { this.memory.set(this.value, value); }
    writeU64(value) { this.memory.set(this.value, value instanceof Handle ? value.value : value); }
}
class Handle {
    constructor(value) { this.value = BigInt(value); }
    equals(other) { return this.value === BigInt(other instanceof Handle ? other.value : other); }
}

// Positive native resolution exercises the actual disassembly/data-flow code,
// then a changed side-flag branch must be refused without attaching anything.
function nativeFixture(wrongSide = false) {
    const rows = new Map();
    const row = (address, mnemonic, opStr) => rows.set(address, {mnemonic, opStr});
    row(492, 'add', 'x2, sp, #0x20'); row(496, 'ldr', 'w8, [sp, #0x40]');
    row(500, 'ldr', 'w1, [sp, #0x30]'); row(504, 'mov', 'x0, x19');
    row(508, 'strb', 'wzr, [sp, #0x20]'); row(512, 'bl', '#0x1000');
    row(516, 'tbz', 'w8, #2, #0x220'); row(520, 'ldrb', 'w9, [sp, #0x20]');
    row(524, 'str', 'w9, [sp, #0x50]'); row(528, 'b', '#0x22c');
    row(544, 'tbz', 'w8, #' + (wrongSide ? 4 : 3) + ', #0x22c');
    row(548, 'ldrb', 'w10, [sp, #0x20]'); row(552, 'str', 'w10, [sp, #0x54]');
    row(556, 'ldrb', 'w11, [sp, #0x20]');
    for (let index = 0; index < 4; index++) row(560 + index * 4, 'mov', 'x' + (12 + index) + ', #0x' + ((index + 1) * 256).toString(16));
    row(576, 'tst', 'w11, #1'); row(580, 'csel', 'x16, x12, x13, ne');
    row(584, 'csel', 'x17, x14, x15, ne'); row(588, 'tst', 'w8, #4');
    row(592, 'csel', 'x18, x16, x17, ne'); row(596, 'add', 'x0, x25, x18');
    const functionRows = [
        ['mov', 'x19, x0'], ['mov', 'w20, w1'], ['mov', 'x21, x2'], ['nop', ''],
        ['ldr', 'x0, [x19, #8]'], ['cbz', 'x0, #0x1030'], ['ldr', 'x22, [x0]'],
        ['ldr', 'x23, [x22, #16]'], ['mov', 'w1, w20'], ['mov', 'x2, x21'],
        ['blr', 'x23'], ['mov', 'w24, w0'], ['mov', 'w0, w24'], ['ret', '']
    ];
    functionRows.forEach(([mnemonic, operands], index) => row(4096 + index * 4, mnemonic, operands));
    let hooks = 0;
    const sandbox = vm.createContext({
        QPRO_PROFILE: profile, rpc: {exports: {}}, ptr: value => new Address(Number(value)),
        Process: {arch: 'arm64', getModuleByName: () => ({enumerateRanges: () => [{base: new Address(0), size: 65536}]})},
        Instruction: {parse: address => {if (!rows.has(address.value)) throw Error('Unknown synthetic instruction'); return rows.get(address.value);}},
        Memory: {scanSync: () => [{address: new Address(512)}]},
        Interceptor: {attach: () => {hooks++; return {detach() {}};}}, setTimeout: () => 1, clearTimeout() {}
    });
    vm.runInContext(fs.readFileSync(path.join(directory, 'quest_controller_adapter.js'), 'utf8'), sandbox);
    return {sandbox, hookCount: () => hooks};
}
const validNative = nativeFixture();
assert.equal(validNative.sandbox.rpc.exports.validate().compatible, true);
assert.equal(validNative.hookCount(), 0);
validNative.sandbox.rpc.exports.activate(20);
assert.equal(validNative.hookCount(), 1);
validNative.sandbox.rpc.exports.deactivate();
const wrongNative = nativeFixture(true);
assert.throws(() => wrongNative.sandbox.rpc.exports.validate(), /ambiguous or unsupported/);
assert.equal(wrongNative.hookCount(), 0);

// Synthetic PC memory exercises the actual role checks, hand-handle routing,
// loss of frame data and restoration; no proprietary binary is loaded.
const bytes = new Map(), address = value => new Address(value, bytes), base = 1000000;
const layout = profile.pcLayout;
const write = (pointer, offset, value) => bytes.set(pointer + offset, value);
write(base, layout.driverContext, 20000); write(20000, 0, 21000); write(21000, 0, 31000);
write(22000, 0, 23000); write(23000, 48, 32000);
write(base, layout.driverHost, 24000); write(24000, 0, 25000); write(25000, 8, 33000);
write(base, layout.controllerTable, 26000); write(base, layout.skeletonTable, 100n);
write(26000, layout.handPointer, 27000); write(26000, layout.role, 1); write(27000, layout.role, 1);
write(26000, layout.deviceIndex, 2); write(27000, layout.deviceIndex, 3);
write(26000, layout.skeleton, 100n); write(27000, layout.skeleton, 200n);
write(26000, layout.dataPointer, 28000); write(28000, 0, 29000);
write(28000, layout.opticalFlags, 1); write(29000, layout.frameFlags, 1);
const observed = {replacements: 0, reverts: 0, pose: null, update: null, tick: null};
let allocation = 4000000;
const driver = {base: address(base), size: 1000000};
const pcSandbox = vm.createContext({
    QPRO_PROFILE: profile, rpc: {exports: {}}, ptr: address,
    Process: {pointerSize: 8, getModuleByName: () => driver, findModuleByName: () => driver,
        findRangeByAddress: () => ({base: address(0), size: 8000000, protection: 'rwx'}),
        attachModuleObserver: () => ({detach() {}})},
    Memory: {alloc: size => {const value = address(allocation); allocation += size; return value;}, allocUtf8String: text => text},
    NativeFunction: function(pointer) {return pointer.value === 31000 ? () => address(22000) : () => 0;},
    NativeCallback: function(callback) {return callback;},
    Interceptor: {replace(pointer, callback) {observed.replacements++; observed.update = callback;},
        revert() {observed.reverts++;}, attach(pointer, callback) {observed.pose = callback; return {detach() {}};}},
    setTimeout: () => 1, clearTimeout() {}, setInterval: callback => {observed.tick = callback; return 1;}, clearInterval() {}
});
vm.runInContext(fs.readFileSync(path.join(directory, 'steamvr_skeleton_adapter.js'), 'utf8'), pcSandbox);
assert.equal(pcSandbox.rpc.exports.validate().compatible, true);
assert.equal(observed.replacements, 0);
pcSandbox.rpc.exports.activate(20); observed.tick();
assert.equal(bytes.get(27000 + layout.skeleton), 100n);
assert.equal(observed.update(address(22000), new Handle(100), 0, address(34000), 31), 0);
observed.tick();
assert.equal(pcSandbox.rpc.exports.status().activeSides[0], true);
assert.equal(pcSandbox.rpc.exports.status().skeletonSubmissions[0], 1);
observed.update(address(22000), new Handle(100), 0, address(34000), 30);
assert.equal(pcSandbox.rpc.exports.status().skeletonSubmissions[0], 1);
write(28000, 0, 0); observed.tick();
assert.equal(bytes.get(27000 + layout.skeleton), 200n);
assert.equal(pcSandbox.rpc.exports.status().activeSides[0], false);
pcSandbox.rpc.exports.deactivate();
assert.equal(pcSandbox.rpc.exports.status().state, 'stopped');
assert.equal(observed.reverts, 1);
assert.equal(bytes.get(26000 + layout.multiModal), 0);
console.log('Hybrid adapters: admitted synthetic ABI, changed ABI refusal, PC31-bone routing, frame loss and original restoration passed.');
