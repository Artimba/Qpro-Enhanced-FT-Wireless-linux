#include "../settings.hpp"
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>

int checks = 0;
void expect(bool okay, const char *name) { ++checks; if (!okay) throw std::runtime_error(name); }
bool near(float a, float b) { return std::fabs(a - b) < .0001f; }
int main() {
    try {
        QptpPacket input{}; input.sequence = 42; input.monotonic_ns = 5000000000;
        input.sides[0] = { QPTP_VALID | QPTP_CONTACT, .5f, -.25f, .8f, 44 };
        input.sides[1] = { QPTP_VALID, -.5f, .25f, .1f, 12 };
        uint8_t wire[64]; qptp_encode(&input, wire); QptpPacket decoded;
        expect(wire[8] == 42 && wire[4] == 1 && wire[6] == 64, "wire little-endian header");
        expect(qptp_decode(wire, 64, &decoded) && decoded.sequence == 42, "round-trip packet");
        expect(decoded.sides[0].flags == 3 && near(decoded.sides[0].x, .5f) && near(decoded.sides[1].force, .1f), "independent sides");
        expect(!qptp_decode(wire, 63, &decoded) && !qptp_decode(wire, 65, &decoded), "exact frame length");
        wire[4] = 2; expect(!qptp_decode(wire, 64, &decoded), "unknown version rejected"); qptp_encode(&input, wire);
        wire[0] = 'X'; expect(!qptp_decode(wire, 64, &decoded), "wrong magic rejected"); qptp_encode(&input, wire);
        qptp_put_float(wire + 28, std::numeric_limits<float>::quiet_NaN()); expect(!qptp_decode(wire, 64, &decoded), "NaN rejected"); qptp_encode(&input, wire);
        qptp_put_float(wire + 40, 513); expect(!qptp_decode(wire, 64, &decoded), "size range bounded"); qptp_encode(&input, wire);
        qptp_put_u32(wire + 24, QPTP_CONTACT); expect(!qptp_decode(wire, 64, &decoded), "contact needs valid sample"); qptp_encode(&input, wire);
        qptp_put_u32(wire + 24, 8); expect(!qptp_decode(wire, 64, &decoded), "reserved flags rejected");

        using namespace qpro_controller;
        PacketLease lease;
        expect(lease.accept(input, 1000000000), "first packet accepted");
        expect(!lease.accept(input, 1000000001), "duplicate rejected");
        auto next = input; next.sequence++; next.monotonic_ns++;
        expect(lease.accept(next, 1010000000), "increasing packet accepted");
        expect(lease.current(1209999999, decoded), "fresh packet available before lease boundary");
        expect(!lease.current(1210000000, decoded), "stale at200ms boundary");
        next.sequence = 1; next.monotonic_ns++;
        expect(lease.accept(next, 1210000001), "reader restart only after expiry and advancing source time");
        expect(!lease.accept(input, 1210000002), "old capture replay rejected after restart");
        expect(!lease.accept(next, 1410000001), "exact duplicate cannot renew expired lease");
        auto reboot = input; reboot.sequence = 1; reboot.monotonic_ns = 1000;
        expect(lease.accept(reboot, 1410000002), "new headset clock epoch allowed only after expiry");
        reboot.sequence++; reboot.monotonic_ns--;
        expect(!lease.accept(reboot, 1410000003), "source clock cannot regress inside new active epoch");
        reboot.monotonic_ns = 1001;
        expect(lease.accept(reboot, 1410000004), "new epoch accepts increasing sequence and clock");
        lease.clear(); expect(!lease.current(1210000002, decoded), "shutdown clears lease");

        Settings settings; settings.smoothing_ms = 0;
        ContactFilter filter; uint64_t tick = 1000000000;
        auto output = filter.step(input.sides[0], tick, settings);
        expect(output.touch && output.click && near(output.x, .5f) && near(output.y, -.25f), "absolute position with pressure click");
        auto side = input.sides[0]; side.force = .5f;
        expect(filter.step(side, tick += 20000000, settings).click, "click stays below onset through hysteresis");
        side.force = .4f; expect(!filter.step(side, tick += 20000000, settings).click, "click releases below lower threshold");
        side.flags = QPTP_VALID; output = filter.step(side, tick += 20000000, settings);
        expect(!output.touch && !output.click && output.x == 0 && output.y == 0 && output.force == 0, "release returns neutral");
        side.flags = 3; settings.mode = Mode::Joystick; output = filter.step(side, tick += 20000000, settings);
        expect(output.x == 0 && output.y == 0, "relative joystick anchors initial touch");
        side.x = -1; output = filter.step(side, tick += 20000000, settings);
        expect(output.x == -1, "relative joystick clamps displacement");
        side.flags = 0; output = filter.step(side, tick += 20000000, settings);
        expect(!output.touch && !output.click && output.x == 0, "invalid sample cannot retain output");
        settings.mode = Mode::Swipe; side = input.sides[0]; filter.step(side, tick += 20000000, settings);
        side.x += .1f; output = filter.step(side, tick += 20000000, settings);
        expect(output.touch && output.x > 0, "swipe responds to movement");
        filter.clear(); output = filter.step(QptpSide{}, tick += 20000000, settings);
        expect(!output.touch && output.x == 0, "expiry clears swipe inertia");
        settings.mode = Mode::Trackpad; settings.ignore_resting_thumb = true; side.flags = 3; side.size = 120; side.force = .1f;
        expect(!filter.step(side, tick += 20000000, settings).touch, "optional broad resting contact rejected");
        side.force = .4f; expect(filter.step(side, tick += 20000000, settings).touch, "intentional pressure overrides resting filter");

        Configuration config;
        expect(parse_settings("{}", config) && !config.enabled && config.input.mode == Mode::Trackpad, "default disabled trackpad");
        expect(parse_settings("{\"enabled\":true,\"mode\":\"mouse\",\"smoothingMs\":0}", config) && config.enabled && config.input.mode == Mode::Mouse, "mouse needs explicit selection");
        expect(!parse_settings("{\"enabled\":true,\"mode\":\"unknown\"}", config), "unknown mode rejected");
        expect(!parse_settings("{\"clickOn\":0.3,\"clickOff\":0.4}", config), "reversed click limits rejected");
        expect(!parse_settings("{\"enabled\":true,\"enabled\":false}", config), "duplicate key rejected");
        expect(!parse_settings("{\"glideMs\":nan}", config), "nonfinite setting rejected");
        expect(!parse_settings("{\"mode\":\"mouse\"}junk", config), "trailing content rejected");
        MouseMotion mouse; Output cursor; cursor.touch = cursor.click = true; cursor.x = .1f;
        auto move = mouse.step(cursor, false, 900); expect(!move.down && move.dx == 0, "mouse default cannot emit movement");
        move = mouse.step(cursor, true, 900); expect(move.down && move.dx == 0, "mouse first touch anchors and presses");
        cursor.x = .2f; move = mouse.step(cursor, true, 900); expect(move.dx >= 89 && !move.down, "mouse movement only after anchor");
        move = mouse.step({}, false, 900); expect(move.up && !move.down && !move.dx, "stop releases mouse button");
        expect(!mouse.step({}, false, 900).up, "shutdown release is idempotent");
        std::cout << "PASS " << checks << " controller packet/contact/settings/shutdown checks\n"; return 0;
    } catch (const std::exception &error) { std::cerr << "FAIL " << error.what() << '\n'; return 1; }
}
