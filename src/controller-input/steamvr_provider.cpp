#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <windows.h>
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wunused-parameter"
#include "third_party/openvr/openvr_driver.h"
#pragma clang diagnostic pop
#include "settings.hpp"
#include <array>
#include <atomic>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

namespace {
using namespace vr;
using namespace qpro_controller;
constexpr char profile[] = "{qpro_controller}/input/quest_pro_touchpad.json";
uint64_t clock_ns() {
    return (uint64_t)std::chrono::duration_cast<std::chrono::nanoseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}
void log(const std::string &text) { VRDriverLog()->Log(("[qpro-controller] " + text).c_str()); }
struct Controls {
    PropertyContainerHandle_t container = 0;
    TrackedDeviceIndex_t device = k_unTrackedDeviceIndexInvalid;
    std::array<VRInputComponentHandle_t, 5> handles{};
    std::string original_profile;
    ContactFilter filter;
    MouseMotion mouse;
};
std::mutex state_mutex;
std::array<Controls, 2> hands;
PacketLease lease;
Configuration configuration;
bool context_ready = false;
std::atomic<bool> running{false};
std::thread receiver;
std::filesystem::path config_path;

Configuration load_configuration() {
    Configuration value;
    std::ifstream input(config_path, std::ios::binary);
    if (!input) return value;
    std::string text; char buffer[1024];
    while (input.read(buffer, sizeof buffer) || input.gcount()) {
        text.append(buffer, (size_t)input.gcount()); if (text.size() > 8192) return value;
    }
    if (!parse_settings(text, value)) return {};
    return value;
}
void mouse_input(const MouseUpdate &change) {
    INPUT event{}; event.type = INPUT_MOUSE;
    event.mi.dx = change.dx; event.mi.dy = change.dy;
    event.mi.dwFlags = (change.dx || change.dy ? MOUSEEVENTF_MOVE : 0)
        | (change.down ? MOUSEEVENTF_LEFTDOWN : 0) | (change.up ? MOUSEEVENTF_LEFTUP : 0);
    if (event.mi.dwFlags) SendInput(1, &event, sizeof event);
}
void publish(const Controls &hand, const Output &value) {
    if (!hand.container) return;
    auto *input = VRDriverInput();
    input->UpdateScalarComponent(hand.handles[0], value.x, 0);
    input->UpdateScalarComponent(hand.handles[1], value.y, 0);
    input->UpdateScalarComponent(hand.handles[2], value.force, 0);
    input->UpdateBooleanComponent(hand.handles[3], value.touch, 0);
    input->UpdateBooleanComponent(hand.handles[4], value.click, 0);
}
void release_hand(Controls &hand) {
    mouse_input(hand.mouse.step({}, false, 0)); publish(hand, {});
    if (hand.container && VRProperties()->GetStringProperty(hand.container, Prop_InputProfilePath_String) == profile)
        VRProperties()->SetStringProperty(hand.container, Prop_InputProfilePath_String, hand.original_profile.c_str());
    hand = {};
}
void deactivate(PropertyContainerHandle_t container) {
    std::lock_guard<std::mutex> guard(state_mutex);
    if (!context_ready || !container) return;
    for (auto &hand : hands) if (hand.container == container) release_hand(hand);
}
PropertyContainerHandle_t remember_container(TrackedDeviceIndex_t id) {
    std::lock_guard<std::mutex> guard(state_mutex);
    return context_ready ? VRProperties()->TrackedDeviceToPropertyContainer(id) : 0;
}
void adopt(TrackedDeviceIndex_t id) {
    std::lock_guard<std::mutex> guard(state_mutex);
    if (!context_ready || !configuration.enabled) return;
    auto *properties = VRProperties(); auto container = properties->TrackedDeviceToPropertyContainer(id);
    if (properties->GetInt32Property(container, Prop_DeviceClass_Int32) != TrackedDeviceClass_Controller) return;
    auto model = properties->GetStringProperty(container, Prop_RenderModelName_String);
    auto native_profile = properties->GetStringProperty(container, Prop_InputProfilePath_String);
    auto role = properties->GetInt32Property(container, Prop_ControllerRoleHint_Int32);
    if (model.find("quest_pro") == std::string::npos || native_profile != "{oculus}/input/touch_profile.json"
        || (role != TrackedControllerRole_LeftHand && role != TrackedControllerRole_RightHand)) return;
    int side = role == TrackedControllerRole_RightHand;
    if (hands[side].container == container) return;
    // Existing controllers can have been activated before our provider loaded.
    // A new container for the same side must not inherit old component handles.
    if (hands[side].container) release_hand(hands[side]);
    Controls pending; pending.container = container; pending.device = id; pending.original_profile = native_profile;
    auto *input = VRDriverInput(); bool valid = true;
    auto scalar = [&](int index, const char *path, EVRScalarUnits units) {
        valid &= input->CreateScalarComponent(container, path, &pending.handles[index], VRScalarType_Absolute, units) == VRInputError_None;
    };
    scalar(0, "/input/trackpad/x", VRScalarUnits_NormalizedTwoSided);
    scalar(1, "/input/trackpad/y", VRScalarUnits_NormalizedTwoSided);
    scalar(2, "/input/trackpad/force", VRScalarUnits_NormalizedOneSided);
    valid &= input->CreateBooleanComponent(container, "/input/trackpad/touch", &pending.handles[3]) == VRInputError_None;
    valid &= input->CreateBooleanComponent(container, "/input/trackpad/click", &pending.handles[4]) == VRInputError_None;
    if (!valid) { log("input creation rejected; original controller profile retained"); return; }
    if (properties->SetStringProperty(container, Prop_InputProfilePath_String, profile) != TrackedProp_Success) {
        log("profile change rejected; original controller type retained"); return;
    }
    // Keep Prop_ControllerType_String unchanged, preserving the ordinary Touch binding identity.
    hands[side] = std::move(pending); publish(hands[side], {});
    log(std::string(side ? "right" : "left") + " Touch Pro inputs attached; application binding may require controller reconnect");
}

// Wrap the public server-driver ABI, forwarding every original operation. There
// are no Virtual Desktop private offsets and no changes to controller poses.
class ControllerAdapter final : public ITrackedDeviceServerDriver {
    ITrackedDeviceServerDriver *original_;
    PropertyContainerHandle_t container_ = 0;
public:
    explicit ControllerAdapter(ITrackedDeviceServerDriver *original) : original_(original) {}
    EVRInitError Activate(uint32_t id) override {
        auto error = original_->Activate(id);
        if (error == VRInitError_None) { container_ = remember_container(id); adopt(id); }
        return error;
    }
    void Deactivate() override { deactivate(container_); container_ = 0; original_->Deactivate(); }
    void EnterStandby() override { original_->EnterStandby(); }
    void *GetComponent(const char *name) override { return original_->GetComponent(name); }
    void DebugRequest(const char *request, char *response, uint32_t length) override { original_->DebugRequest(request, response, length); }
    DriverPose_t GetPose() override { return original_->GetPose(); }
};
using DeviceAdded = bool (*)(IVRServerDriverHost *, const char *, ETrackedDeviceClass, ITrackedDeviceServerDriver *);
DeviceAdded previous_added = nullptr;
void **added_slot = nullptr;
std::mutex adapters_mutex;
std::vector<std::unique_ptr<ControllerAdapter>> adapters;
bool device_added(IVRServerDriverHost *host, const char *serial, ETrackedDeviceClass type, ITrackedDeviceServerDriver *device) {
    if (device && type == TrackedDeviceClass_Controller) {
        std::lock_guard<std::mutex> guard(adapters_mutex);
        adapters.push_back(std::make_unique<ControllerAdapter>(device)); device = adapters.back().get();
    }
    // Do not retain our adapter lock across the original activation callback.
    return previous_added(host, serial, type, device);
}
bool replace_slot(void *expected, void *replacement) {
    DWORD protection; if (!added_slot || !VirtualProtect(added_slot, sizeof(void *), PAGE_READWRITE, &protection)) return false;
    void *observed = InterlockedCompareExchangePointer((void *volatile *)added_slot, replacement, expected);
    DWORD discarded; VirtualProtect(added_slot, sizeof(void *), protection, &discarded);
    return observed == expected;
}
bool attach_host() {
    auto *host = (IVRServerDriverHost *)VRDriverContext()->GetGenericInterface(IVRServerDriverHost_Version);
    if (!host) return false;
    // TrackedDeviceAdded is the first method in this pinned, public OpenVR interface.
    added_slot = *reinterpret_cast<void ***>(host); previous_added = reinterpret_cast<DeviceAdded>(*added_slot);
    return replace_slot(reinterpret_cast<void *>(previous_added), reinterpret_cast<void *>(&device_added));
}
void receive_packets(SOCKET socket) {
    while (running) {
        fd_set readable; FD_ZERO(&readable); FD_SET(socket, &readable); timeval delay{0, 50000};
        if (select(0, &readable, nullptr, nullptr, &delay) <= 0) continue;
        uint8_t bytes[QPTP_PACKET_BYTES + 1]; sockaddr_in sender{}; int size = sizeof sender;
        int count = recvfrom(socket, (char *)bytes, sizeof bytes, 0, (sockaddr *)&sender, &size);
        QptpPacket value;
        if (sender.sin_addr.s_addr != htonl(INADDR_LOOPBACK) || count != QPTP_PACKET_BYTES
            || !qptp_decode(bytes, (size_t)count, &value)) continue;
        std::lock_guard<std::mutex> guard(state_mutex); lease.accept(value, clock_ns());
    }
    closesocket(socket);
}
class ControllerProvider final : public IServerTrackedDeviceProvider {
    uint64_t config_read_ = 0;
public:
    EVRInitError Init(IVRDriverContext *context) override {
        VR_INIT_SERVER_DRIVER_CONTEXT(context);
        HMODULE module = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&clock_ns), &module)) return VRInitError_Driver_Failed;
        wchar_t path[32768]; DWORD length = GetModuleFileNameW(module, path, 32768);
        if (!length || length >= 32768) return VRInitError_Driver_Failed;
        config_path = std::filesystem::path(path).parent_path().parent_path().parent_path() / "resources" / "settings.json";
        configuration = load_configuration();
        WSADATA data; if (WSAStartup(MAKEWORD(2, 2), &data)) return VRInitError_Driver_Failed;
        SOCKET socket = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
        sockaddr_in address{}; address.sin_family = AF_INET; address.sin_port = htons(27064); address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        if (socket == INVALID_SOCKET || bind(socket, (sockaddr *)&address, sizeof address) == SOCKET_ERROR) {
            if (socket != INVALID_SOCKET) closesocket(socket); WSACleanup(); log("UDP27064 unavailable; controller addon inactive"); return VRInitError_Driver_Failed;
        }
        if (!attach_host()) { closesocket(socket); WSACleanup(); log("public OpenVR host adoption unavailable"); return VRInitError_Driver_Failed; }
        { std::lock_guard<std::mutex> guard(state_mutex); context_ready = true; }
        running = true;
        try { receiver = std::thread(receive_packets, socket); }
        catch (...) {
            running = false;
            replace_slot(reinterpret_cast<void *>(&device_added), reinterpret_cast<void *>(previous_added));
            { std::lock_guard<std::mutex> guard(state_mutex); context_ready = false; }
            closesocket(socket); WSACleanup(); log("receiver thread unavailable; addon startup unwound");
            VR_CLEANUP_SERVER_DRIVER_CONTEXT(); return VRInitError_Driver_Failed;
        }
        log("experimental addon ready; default disabled, waiting for opted-in Qpro input"); return VRInitError_None;
    }
    void Cleanup() override {
        running = false; if (receiver.joinable()) receiver.join();
        replace_slot(reinterpret_cast<void *>(&device_added), reinterpret_cast<void *>(previous_added));
        std::lock_guard<std::mutex> guard(state_mutex);
        if (!context_ready) return;
        context_ready = false;
        for (auto &hand : hands) release_hand(hand);
        lease.clear(); WSACleanup(); VR_CLEANUP_SERVER_DRIVER_CONTEXT();
        // SteamVR may still hold forwarded driver pointers during shutdown. The
        // adapters live through process teardown; hot unloading is unsupported.
    }
    const char *const *GetInterfaceVersions() override { return k_InterfaceVersions; }
    void RunFrame() override {
        uint64_t now = clock_ns();
        if (now - config_read_ >= 1000000000) {
            Configuration next = load_configuration();
            { std::lock_guard<std::mutex> guard(state_mutex); configuration = next; }
            config_read_ = now;
            // This also discovers controllers activated before the user enabled
            // the option. Existing profile/binding caches may need reconnecting.
            if (next.enabled) for (uint32_t id = 0; id < k_unMaxTrackedDeviceCount; ++id) adopt(id);
        }
        std::lock_guard<std::mutex> guard(state_mutex); QptpPacket packet;
        bool fresh = configuration.enabled && lease.current(now, packet);
        for (int side = 0; side < 2; ++side) {
            auto &hand = hands[side]; Output output;
            if (fresh) output = hand.filter.step(packet.sides[side], now, configuration.input);
            else hand.filter.clear();
            bool mouse = fresh && configuration.input.mode == Mode::Mouse;
            // One pointer/button owner avoids one thumb releasing the other
            // thumb's desktop click. Mouse mode uses only the right controller.
            mouse_input(hand.mouse.step(output, mouse && side == 1, configuration.input.mouse_scale));
            publish(hand, mouse ? Output{} : output);
        }
    }
    bool ShouldBlockStandbyMode() override { return false; }
    void EnterStandby() override {}
    void LeaveStandby() override {}
};
ControllerProvider provider;
}
extern "C" __declspec(dllexport) void *HmdDriverFactory(const char *name, int *error) {
    if (name && !strcmp(name, vr::IServerTrackedDeviceProvider_Version)) return &provider;
    if (error) *error = vr::VRInitError_Init_InterfaceNotFound; return nullptr;
}
