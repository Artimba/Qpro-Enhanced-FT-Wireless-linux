#pragma once
#include "controller_logic.hpp"
#include <cctype>
#include <cstdlib>
#include <string>

namespace qpro_controller {
struct Configuration { bool enabled = false; Settings input; };

// Deliberately accept only a flat, bounded object with this add-on's known keys.
// An invalid file disables input; settings from a partly parsed file never escape.
inline bool parse_settings(const std::string &text, Configuration &result) {
    if (text.size() > 8192) return false;
    size_t cursor = 0;
    auto space = [&] { while (cursor < text.size() && std::isspace((unsigned char)text[cursor])) ++cursor; };
    auto take = [&](char c) { space(); if (cursor == text.size() || text[cursor] != c) return false; ++cursor; return true; };
    auto quoted = [&](std::string &value) {
        if (!take('"')) return false; size_t begin = cursor;
        while (cursor < text.size() && text[cursor] != '"') {
            if (text[cursor] == '\\' || (unsigned char)text[cursor] < 32) return false; ++cursor;
        }
        if (cursor == text.size()) return false;
        value = text.substr(begin, cursor - begin); ++cursor; return true;
    };
    Configuration parsed;
    std::string seen = "|";
    if (!take('{')) return false;
    space();
    if (cursor < text.size() && text[cursor] != '}') for (;;) {
        std::string key; if (!quoted(key) || !take(':') || seen.find("|" + key + "|") != std::string::npos) return false;
        seen += key + "|"; space();
        if (key == "mode") {
            std::string mode; if (!quoted(mode)) return false;
            if (mode == "trackpad") parsed.input.mode = Mode::Trackpad;
            else if (mode == "joystick") parsed.input.mode = Mode::Joystick;
            else if (mode == "swipe") parsed.input.mode = Mode::Swipe;
            else if (mode == "mouse") parsed.input.mode = Mode::Mouse;
            else return false;
        } else if (key == "enabled" || key == "ignoreRestingThumb") {
            bool value;
            if (text.compare(cursor, 4, "true") == 0) { value = true; cursor += 4; }
            else if (text.compare(cursor, 5, "false") == 0) { value = false; cursor += 5; }
            else return false;
            if (key == "enabled") parsed.enabled = value; else parsed.input.ignore_resting_thumb = value;
        } else {
            if (cursor == text.size() || (text[cursor] != '-' && !std::isdigit((unsigned char)text[cursor]))) return false;
            char *end; double value = std::strtod(text.c_str() + cursor, &end);
            if (end == text.c_str() + cursor || !std::isfinite(value)) return false;
            cursor = (size_t)(end - text.c_str()); float *target = nullptr; float low = 0, high = 1;
            if (key == "smoothingMs") { target = &parsed.input.smoothing_ms; high = 250; }
            else if (key == "clickOn") target = &parsed.input.click_on;
            else if (key == "clickOff") target = &parsed.input.click_off;
            else if (key == "joystickSpan") { target = &parsed.input.joystick_span; low = .1f; high = 2; }
            else if (key == "swipeGain") { target = &parsed.input.swipe_gain; low = .01f; high = 5; }
            else if (key == "glideMs") { target = &parsed.input.glide_ms; high = 2000; }
            else if (key == "mouseScale") { target = &parsed.input.mouse_scale; high = 4000; }
            else if (key == "restingSize") { target = &parsed.input.resting_size; high = 512; }
            else if (key == "intentionalForce") target = &parsed.input.intentional_force;
            else return false;
            if (value < low || value > high) return false; *target = (float)value;
        }
        space(); if (cursor < text.size() && text[cursor] == '}') break;
        if (!take(',')) return false;
    }
    if (!take('}')) return false; space();
    if (cursor != text.size() || parsed.input.click_off >= parsed.input.click_on) return false;
    result = parsed; return true;
}
struct MouseUpdate { int dx = 0, dy = 0; bool down = false, up = false; };
class MouseMotion {
    bool active_ = false, pressed_ = false;
    float x_ = 0, y_ = 0, carry_x_ = 0, carry_y_ = 0;
public:
    MouseUpdate step(const Output &value, bool allowed, float scale) {
        MouseUpdate update;
        if (!allowed || !value.touch) {
            update.up = pressed_; pressed_ = active_ = false; carry_x_ = carry_y_ = 0; return update;
        }
        if (active_) {
            carry_x_ += (value.x - x_) * scale; carry_y_ -= (value.y - y_) * scale;
            update.dx = (int)carry_x_; update.dy = (int)carry_y_;
            carry_x_ -= update.dx; carry_y_ -= update.dy;
        }
        x_ = value.x; y_ = value.y; active_ = true;
        update.down = value.click && !pressed_; update.up = !value.click && pressed_; pressed_ = value.click;
        return update;
    }
};
}
