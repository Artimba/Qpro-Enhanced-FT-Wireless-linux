#pragma once
#include "protocol.h"
#include <algorithm>
#include <cmath>
#include <cstdint>

namespace qpro_controller {
enum class Mode { Trackpad, Joystick, Swipe, Mouse };
struct Settings {
    Mode mode = Mode::Trackpad;
    float smoothing_ms = 18, click_on = .64f, click_off = .46f;
    float joystick_span = .6f, swipe_gain = .22f, glide_ms = 180, mouse_scale = 900;
    bool ignore_resting_thumb = false;
    float resting_size = 96, intentional_force = .28f;
};
struct Output { float x = 0, y = 0, force = 0; bool touch = false, click = false; };

// Arrival time is local: headset and Windows monotonic clocks have different epochs.
class PacketLease {
    QptpPacket packet_{};
    uint64_t arrival_ = 0;
    bool have_ = false;
public:
    static constexpr uint64_t stale_ns = 200000000;
    bool accept(const QptpPacket &value, uint64_t now) {
        if (have_) {
            // A restarted reader or headset can have a new counter/clock epoch.
            // Accept that only once the old lease has expired. The transport is
            // trusted loopback; this is ordering protection, not authentication.
            if (value.sequence == packet_.sequence && value.monotonic_ns == packet_.monotonic_ns) return false;
            bool active = now < arrival_ || now - arrival_ < stale_ns;
            if (active && (value.monotonic_ns <= packet_.monotonic_ns || value.sequence <= packet_.sequence)) return false;
        }
        packet_ = value; arrival_ = now; have_ = true; return true;
    }
    bool current(uint64_t now, QptpPacket &out) const {
        if (!have_ || now < arrival_ || now - arrival_ >= stale_ns) return false;
        out = packet_; return true;
    }
    void clear() { packet_ = {}; arrival_ = 0; have_ = false; }
};

class ContactFilter {
    bool touching_ = false, clicked_ = false;
    float x_ = 0, y_ = 0, anchor_x_ = 0, anchor_y_ = 0, vx_ = 0, vy_ = 0;
    uint64_t tick_ = 0;
    Mode mode_ = Mode::Trackpad;
public:
    void clear() { *this = ContactFilter{}; }
    Output step(const QptpSide &sample, uint64_t now, const Settings &settings) {
        if (mode_ != settings.mode) { clear(); mode_ = settings.mode; }
        if (!(sample.flags & QPTP_VALID)) { clear(); return {}; }
        bool touch = (sample.flags & QPTP_CONTACT) != 0;
        if (settings.ignore_resting_thumb && sample.size > settings.resting_size
            && sample.force < settings.intentional_force) touch = false;
        float seconds = tick_ && now > tick_ ? std::min(.1f, float(now - tick_) / 1e9f) : 1.f / 60;
        tick_ = now;
        if (touch) {
            if (!touching_) {
                x_ = anchor_x_ = sample.x; y_ = anchor_y_ = sample.y; vx_ = vy_ = 0;
            } else {
                float blend = settings.smoothing_ms <= 0 ? 1.f : -std::expm1(-seconds * 1000 / settings.smoothing_ms);
                float nx = x_ + blend * (sample.x - x_), ny = y_ + blend * (sample.y - y_);
                vx_ = (nx - x_) / seconds; vy_ = (ny - y_) / seconds; x_ = nx; y_ = ny;
            }
            clicked_ = sample.force >= (clicked_ ? settings.click_off : settings.click_on);
        } else {
            clicked_ = false;
            float fade = settings.glide_ms > 0 ? std::exp(-seconds * 1000 / settings.glide_ms) : 0;
            vx_ *= fade; vy_ *= fade;
        }
        touching_ = touch;
        Output result;
        result.touch = touch; result.click = clicked_; result.force = touch ? sample.force : 0;
        switch (settings.mode) {
        case Mode::Trackpad: case Mode::Mouse:
            result.x = touch ? x_ : 0; result.y = touch ? y_ : 0; break;
        case Mode::Joystick:
            result.x = touch ? std::clamp((x_ - anchor_x_) / settings.joystick_span, -1.f, 1.f) : 0;
            result.y = touch ? std::clamp((y_ - anchor_y_) / settings.joystick_span, -1.f, 1.f) : 0; break;
        case Mode::Swipe:
            result.x = std::clamp(vx_ * settings.swipe_gain, -1.f, 1.f);
            result.y = std::clamp(vy_ * settings.swipe_gain, -1.f, 1.f);
            if (std::fabs(result.x) < .015f) result.x = 0;
            if (std::fabs(result.y) < .015f) result.y = 0;
            result.touch = touch || result.x != 0 || result.y != 0; break;
        }
        return result;
    }
};
}
