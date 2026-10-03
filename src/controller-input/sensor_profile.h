#ifndef QPRO_TOUCHPAD_SENSOR_PROFILE_H
#define QPRO_TOUCHPAD_SENSOR_PROFILE_H
#include "protocol.h"

enum { QPTP_PAGE_SIZE = 4096, QPTP_COUNTER_OFFSET = 0x1e8, QPTP_SAMPLES_OFFSET = 0x1f0, QPTP_SAMPLE_BYTES = 16 };
static inline QptpSide qptp_read_sample(const uint8_t *address, uint32_t *last_counter, uint64_t *changed_ns, uint64_t now) {
    QptpSide result = {0}; if (!address) return result;
    const uint32_t *counter = (const uint32_t *)(const void *)(address + QPTP_COUNTER_OFFSET);
    uint8_t raw[QPTP_SAMPLE_BYTES]; uint32_t sequence = 0; int stable = 0;
    for (int attempt = 0; attempt < 5; ++attempt) {
        sequence = __atomic_load_n(counter, __ATOMIC_ACQUIRE);
        memcpy(raw, address + QPTP_SAMPLES_OFFSET + (sequence % 2) * QPTP_SAMPLE_BYTES, QPTP_SAMPLE_BYTES);
        __atomic_thread_fence(__ATOMIC_ACQUIRE);
        if (sequence == __atomic_load_n(counter, __ATOMIC_RELAXED)) { stable = 1; break; }
    }
    if (!stable || !sequence) return result;
    if (sequence != *last_counter) { *last_counter = sequence; *changed_ns = now; }
    if (!*changed_ns || now < *changed_ns || now - *changed_ns >= UINT64_C(200000000)) return result;
    unsigned int x = qptp_u16(raw), y = qptp_u16(raw + 2);
    result.force = qptp_float(raw + 4); result.size = qptp_float(raw + 8);
    if (x > 255 || y > 255 || (raw[12] & ~3u)) return (QptpSide){0};
    result.x = (float)x / 127.5f - 1.f; result.y = (float)y / 127.5f - 1.f;
    result.flags = QPTP_VALID | ((raw[12] & 1) ? QPTP_CONTACT : 0);
    return qptp_side_valid(&result) ? result : (QptpSide){0};
}
#endif
