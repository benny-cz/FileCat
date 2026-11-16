/* FileCat private validation wrapper (MIT). Only individually pinned public-domain SDK sources are linked. */
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include "Ppmd7.h"

typedef struct { IByteIn interface; const Byte *bytes; size_t size, position; int extra; } Input;
static Byte read_byte(IByteInPtr pointer) {
  Input *input = (Input *)pointer;
  if (input->position == input->size) { input->extra = 1; return 255; }
  return input->bytes[input->position++];
}
static uint64_t little(const Byte *p, unsigned count) { uint64_t value = 0; unsigned i; for (i = 0; i < count; ++i) value |= (uint64_t)p[i] << (8 * i); return value; }
static void *allocate(ISzAllocPtr owner, size_t size) { (void)owner; return size > (32u << 20) ? NULL : malloc(size); }
static void release(ISzAllocPtr owner, void *address) { (void)owner; free(address); }

int main(int argc, char **argv) {
  FILE *file; long size; Byte *bytes, *output; uint64_t length; uint32_t packed, memory, code = 0;
  size_t produced = 0; int accepted = 0; Input input; CPpmd7 model; ISzAlloc allocator = { allocate, release };
  if (argc != 3 || !(file = fopen(argv[1], "rb"))) return 64;
  if (fseek(file, 0, SEEK_END) || (size = ftell(file)) < 17 || size > (4 << 20) || fseek(file, 0, SEEK_SET)) { fclose(file); return 65; }
  bytes = (Byte *)malloc((size_t)size); if (!bytes) { fclose(file); return 66; }
  if (fread(bytes, 1, (size_t)size, file) != (size_t)size) { free(bytes); fclose(file); return 67; }
  fclose(file); length = little(bytes + 5, 8); packed = (uint32_t)little(bytes + 13, 4); memory = (uint32_t)little(bytes + 1, 4);
  if (length > (2 << 20) || (uint64_t)size != 17 + (uint64_t)packed) { free(bytes); return 68; }
  output = (Byte *)malloc(length ? (size_t)length : 1); if (!output) { free(bytes); return 69; }
  input.interface.Read = read_byte; input.bytes = bytes + 17; input.size = packed; input.position = 0; input.extra = 0;
  Ppmd7_Construct(&model);
  if (bytes[0] >= 2 && bytes[0] <= 64 && memory >= (1 << 11) && memory <= (32u << 20) && Ppmd7_Alloc(&model, memory, &allocator)) {
    Ppmd7_Init(&model, bytes[0]); model.rc.dec.Stream = &input.interface;
    if (Ppmd7z_RangeDec_Init(&model.rc.dec) && !input.extra) {
      while (produced < length) {
        int symbol = Ppmd7z_DecodeSymbol(&model);
        if (symbol < 0 || input.extra) break;
        output[produced++] = (Byte)symbol;
      }
      code = model.rc.dec.Code;
      accepted = produced == length && !input.extra && input.position == packed && Ppmd7z_RangeDec_IsFinishedOK(&model.rc.dec);
    }
    Ppmd7_Free(&model, &allocator);
  }
  file = fopen(argv[2], "wb");
  if (!file) { free(output); free(bytes); return 70; }
  if (fwrite(output, 1, produced, file) != produced || fclose(file)) { free(output); free(bytes); return 71; }
  printf("{\"accepted\":%s,\"produced\":%llu,\"consumed\":%llu,\"code\":%u}\n", accepted ? "true" : "false", (unsigned long long)produced, (unsigned long long)input.position, code);
  free(output); free(bytes); return 0;
}
