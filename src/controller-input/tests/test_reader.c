#include "../sensor_profile.h"
#include "../reader_lifetime.h"
#include <stdio.h>

int main(void) {
    uint8_t memory[QPTP_PAGE_SIZE] = {0}; uint32_t last_counter = 0; uint64_t changed_ns = 0;
    uint32_t *counter = (uint32_t *)(void *)(memory + QPTP_COUNTER_OFFSET);
    *counter = 1; uint8_t *raw = memory + QPTP_SAMPLES_OFFSET + QPTP_SAMPLE_BYTES;
    qptp_put_u16(raw, 255); qptp_put_u16(raw + 2, 0); qptp_put_float(raw + 4, .5f);
    qptp_put_float(raw + 8, 30); raw[12] = 1;
    QptpSide read = qptp_read_sample(memory, &last_counter, &changed_ns, 1000000000);
    if (read.flags != 3 || read.x != 1 || read.y != -1 || read.force != .5f) return 1;
    if (qptp_read_sample(memory, &last_counter, &changed_ns, 1200000000).flags) return 2;
    *counter = 2; raw = memory + QPTP_SAMPLES_OFFSET; memcpy(raw, memory + QPTP_SAMPLES_OFFSET + QPTP_SAMPLE_BYTES, QPTP_SAMPLE_BYTES);
    raw[12] = 0; read = qptp_read_sample(memory, &last_counter, &changed_ns, 1210000000);
    if (read.flags != QPTP_VALID) return 3;
    qptp_put_u16(raw, 256); if (qptp_read_sample(memory, &last_counter, &changed_ns, 1220000000).flags) return 4;
    qptp_put_u16(raw, 128); qptp_put_float(raw + 4, NAN); if (qptp_read_sample(memory, &last_counter, &changed_ns, 1230000000).flags) return 5;
    *counter = 0; if (qptp_read_sample(memory, &last_counter, &changed_ns, 1240000000).flags) return 6;
    QptpReaderLifetime lifetime; qptp_lifetime_start(&lifetime, 1000000000);
    if (qptp_lifetime_expired(&lifetime, 15999999999) || !qptp_lifetime_expired(&lifetime, 16000000000)) return 7;
    qptp_lifetime_connect(&lifetime);
    if (qptp_lifetime_expired(&lifetime, 99999999999)) return 8;
    qptp_lifetime_disconnect(&lifetime, 17000000000);
    if (qptp_lifetime_expired(&lifetime, 18999999999) || !qptp_lifetime_expired(&lifetime, 19000000000)) return 9;
    qptp_lifetime_connect(&lifetime);
    if (qptp_lifetime_expired(&lifetime, 19000000000) || !lifetime.ever_connected) return 10;
    qptp_lifetime_disconnect(&lifetime, 20000000000);
    if (!qptp_lifetime_expired(&lifetime, 22000000000)) return 11;
    puts("PASS 11 fake-page freshness/range/finite/double-buffer/lifetime checks"); return 0;
}
