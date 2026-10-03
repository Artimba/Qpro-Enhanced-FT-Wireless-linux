#ifndef QPRO_READER_LIFETIME_H
#define QPRO_READER_LIFETIME_H
#include <stdint.h>

// The supervisor owns each foreground reader. An abandoned instance must not
// keep mappings open indefinitely if ADB or the PC relay disappears.
typedef struct {
    uint64_t deadline_ns;
    int connected, ever_connected;
} QptpReaderLifetime;
static inline void qptp_lifetime_start(QptpReaderLifetime *state, uint64_t now) {
    *state = (QptpReaderLifetime){ now + UINT64_C(15000000000), 0, 0 };
}
static inline void qptp_lifetime_connect(QptpReaderLifetime *state) {
    state->connected = state->ever_connected = 1;
}
static inline void qptp_lifetime_disconnect(QptpReaderLifetime *state, uint64_t now) {
    state->connected = 0; state->deadline_ns = now + UINT64_C(2000000000);
}
static inline int qptp_lifetime_expired(const QptpReaderLifetime *state, uint64_t now) {
    return !state->connected && now >= state->deadline_ns;
}
#endif
