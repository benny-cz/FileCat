/* FileCat private fixture wrapper (MIT); SDK Bcj2.c/Bcj2.h are individually public domain.
   Test-only, bounded public synthetic vectors. Never a production dependency. */
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include "Bcj2.h"

static uint32_t little32(const unsigned char *p) {
  return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24;
}
int main(int argc, char **argv) {
  FILE *input = NULL, *output = NULL; Byte *packet = NULL, *decoded = NULL;
  long size; char *end; unsigned long expected; uint32_t lengths[4]; size_t offset = 16;
  unsigned i; int result = 1; CBcj2Dec decoder;
  if (argc != 4) return 64;
  expected = strtoul(argv[3], &end, 10);
  if (!*argv[3] || *end || expected > (4 << 20)) return 65;
  input = fopen(argv[1], "rb");
  if (!input) return 66;
  if (fseek(input, 0, SEEK_END) || (size = ftell(input)) < 16 || size > (4 << 20) || fseek(input, 0, SEEK_SET)) goto done;
  packet = (Byte *)malloc((size_t)size);
  decoded = (Byte *)calloc((size_t)expected + 1, 1);
  if (!packet || !decoded || fread(packet, 1, (size_t)size, input) != (size_t)size) goto done;
  Bcj2Dec_Init(&decoder);
  for (i = 0; i < 4; i++) {
    lengths[i] = little32(packet + 4 * i);
    if (lengths[i] > (size_t)size - offset) goto done;
    decoder.bufs[i] = packet + offset; offset += lengths[i]; decoder.lims[i] = packet + offset;
  }
  if (offset != (size_t)size || lengths[0] + (uint64_t)lengths[1] + lengths[2] != expected ||
      (lengths[1] & 3) || (lengths[2] & 3)) goto done;
  decoder.dest = decoded; decoder.destLim = decoded + expected;
  if (Bcj2Dec_Decode(&decoder) != SZ_OK || decoder.dest != decoder.destLim || !Bcj2Dec_IsMaybeFinished(&decoder)) goto done;
  for (i = 0; i < 4; i++) if (decoder.bufs[i] != decoder.lims[i]) goto done;
  output = fopen(argv[2], "wb");
  if (!output || fwrite(decoded, 1, expected, output) != expected) goto done;
  result = 0;
done:
  if (input) fclose(input);
  if (output && fclose(output)) result = 1;
  free(packet); free(decoded); return result;
}
