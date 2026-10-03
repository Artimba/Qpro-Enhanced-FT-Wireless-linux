#ifndef QPRO_TOUCHPAD_PROTOCOL_H
#define QPRO_TOUCHPAD_PROTOCOL_H

#include <math.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#define QPTP_PACKET_BYTES 64
#define QPTP_VERSION 1
#define QPTP_VALID 1u
#define QPTP_CONTACT 2u

typedef struct { uint32_t flags; float x, y, force, size; } QptpSide;
typedef struct { uint64_t sequence, monotonic_ns; QptpSide sides[2]; } QptpPacket;

static inline uint16_t qptp_u16(const uint8_t *p) {
    return (uint16_t)(p[0] | ((uint16_t)p[1] << 8));
}
static inline uint32_t qptp_u32(const uint8_t *p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}
static inline uint64_t qptp_u64(const uint8_t *p) {
    return (uint64_t)qptp_u32(p) | ((uint64_t)qptp_u32(p + 4) << 32);
}
static inline float qptp_float(const uint8_t *p) {
    uint32_t bits = qptp_u32(p); float value; memcpy(&value, &bits, 4); return value;
}
static inline void qptp_put_u16(uint8_t *p, uint16_t n) { p[0] = (uint8_t)n; p[1] = (uint8_t)(n >> 8); }
static inline void qptp_put_u32(uint8_t *p, uint32_t n) {
    for (int i = 0; i < 4; ++i) p[i] = (uint8_t)(n >> (i * 8));
}
static inline void qptp_put_u64(uint8_t *p, uint64_t n) {
    qptp_put_u32(p, (uint32_t)n); qptp_put_u32(p + 4, (uint32_t)(n >> 32));
}
static inline void qptp_put_float(uint8_t *p, float n) {
    uint32_t bits; memcpy(&bits, &n, 4); qptp_put_u32(p, bits);
}
static inline int qptp_side_valid(const QptpSide *s) {
    return !(s->flags & ~(QPTP_VALID | QPTP_CONTACT))
        && (!(s->flags & QPTP_CONTACT) || (s->flags & QPTP_VALID))
        && isfinite(s->x) && isfinite(s->y) && isfinite(s->force) && isfinite(s->size)
        && s->x >= -1.f && s->x <= 1.f && s->y >= -1.f && s->y <= 1.f
        && s->force >= 0.f && s->force <= 1.f && s->size >= 0.f && s->size <= 512.f;
}
static inline int qptp_decode(const uint8_t *p, size_t length, QptpPacket *out) {
    if (length != QPTP_PACKET_BYTES || memcmp(p, "QPTP", 4)
        || qptp_u16(p + 4) != QPTP_VERSION || qptp_u16(p + 6) != QPTP_PACKET_BYTES) return 0;
    QptpPacket decoded;
    decoded.sequence = qptp_u64(p + 8); decoded.monotonic_ns = qptp_u64(p + 16);
    if (!decoded.sequence || !decoded.monotonic_ns) return 0;
    for (int i = 0; i < 2; ++i) {
        const uint8_t *s = p + 24 + i * 20;
        decoded.sides[i] = (QptpSide){ qptp_u32(s), qptp_float(s + 4), qptp_float(s + 8),
            qptp_float(s + 12), qptp_float(s + 16) };
        if (!qptp_side_valid(&decoded.sides[i])) return 0;
    }
    *out = decoded; return 1;
}
static inline void qptp_encode(const QptpPacket *in, uint8_t out[QPTP_PACKET_BYTES]) {
    memcpy(out, "QPTP", 4); qptp_put_u16(out + 4, QPTP_VERSION); qptp_put_u16(out + 6, QPTP_PACKET_BYTES);
    qptp_put_u64(out + 8, in->sequence); qptp_put_u64(out + 16, in->monotonic_ns);
    for (int i = 0; i < 2; ++i) {
        uint8_t *s = out + 24 + i * 20; const QptpSide *v = &in->sides[i];
        qptp_put_u32(s, v->flags); qptp_put_float(s + 4, v->x); qptp_put_float(s + 8, v->y);
        qptp_put_float(s + 12, v->force); qptp_put_float(s + 16, v->size);
    }
}
#endif
