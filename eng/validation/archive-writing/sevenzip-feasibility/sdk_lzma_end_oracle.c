/* FileCat test harness (MIT). Calls only pinned public-domain SDK decoder sources; never shipped. */
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include "LzmaDec.h"
#include "Lzma2Dec.h"

static void *allocate(ISzAllocPtr owner, size_t size) { (void)owner; return size > (64u << 20) ? NULL : malloc(size); }
static void release(ISzAllocPtr owner, void *address) { (void)owner; free(address); }
static uint64_t little(const Byte *p, unsigned n) { uint64_t v = 0; unsigned i; for (i = 0; i < n; ++i) v |= (uint64_t)p[i] << (8 * i); return v; }

int main(int argc, char **argv) {
  FILE *file; long size; Byte *bytes, *output; SizeT packed, unpacked; uint64_t claimed;
  ISzAlloc allocator = { allocate, release }; ELzmaStatus status = LZMA_STATUS_NOT_SPECIFIED; SRes result; int accepted;
  if (argc != 3 || !(file = fopen(argv[1], "rb"))) return 64;
  if (fseek(file, 0, SEEK_END) || (size = ftell(file)) < 14 || size > (4 << 20) || fseek(file, 0, SEEK_SET)) { fclose(file); return 65; }
  bytes = (Byte *)malloc((size_t)size); if (!bytes) { fclose(file); return 66; }
  if (fread(bytes, 1, (size_t)size, file) != (size_t)size) { free(bytes); fclose(file); return 67; }
  fclose(file); claimed = little(bytes + 2, 8); packed = (SizeT)little(bytes + 10, 4);
  if (bytes[0] > 1 || bytes[1] != (bytes[0] ? 1 : 5) || claimed > (2 << 20) || (uint64_t)size != 14 + bytes[1] + (uint64_t)packed) { free(bytes); return 68; }
  unpacked = (SizeT)claimed; output = (Byte *)malloc(unpacked ? unpacked : 1); if (!output) { free(bytes); return 69; }
  if (bytes[0]) result = Lzma2Decode(output, &unpacked, bytes + 15, &packed, bytes[14], LZMA_FINISH_END, &status, &allocator);
  else result = LzmaDecode(output, &unpacked, bytes + 19, &packed, bytes + 14, 5, LZMA_FINISH_END, &status, &allocator);
  accepted = result == SZ_OK && unpacked == claimed && packed == little(bytes + 10, 4) &&
    (status == LZMA_STATUS_FINISHED_WITH_MARK || (!bytes[0] && status == LZMA_STATUS_MAYBE_FINISHED_WITHOUT_MARK));
  file = fopen(argv[2], "wb");
  if (!file) { free(output); free(bytes); return 70; }
  if (fwrite(output, 1, unpacked, file) != unpacked || fclose(file)) { free(output); free(bytes); return 71; }
  printf("{\"accepted\":%s,\"result\":%d,\"status\":%d,\"input_consumed\":%llu,\"output_produced\":%llu}\n",
    accepted ? "true" : "false", (int)result, (int)status, (unsigned long long)packed, (unsigned long long)unpacked);
  free(output); free(bytes); return 0;
}
