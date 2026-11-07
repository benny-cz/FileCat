/* FileCat private validation wrapper (MIT), never a runtime dependency.
   Compile only with the exact individually reviewed public-domain SDK C subset in the source ledger. */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include "Bra.h"
#include "Delta.h"
#include "SwapBytes.h"
#include "Ppmd7.h"

static int number(const char *text, unsigned long *out) {
  char *end; unsigned long value = strtoul(text, &end, 10);
  if (!*text || *end || value > 0xffffffffUL) return 0;
  *out = value; return 1;
}
static Byte *load(const char *path, size_t *size) {
  FILE *file = fopen(path, "rb"); long length; Byte *bytes;
  if (!file) return NULL;
  if (fseek(file, 0, SEEK_END) || (length = ftell(file)) < 0 || length > (4 << 20) || fseek(file, 0, SEEK_SET)) { fclose(file); return NULL; }
  *size = (size_t)length; bytes = (Byte *)calloc(*size + 16, 1);
  if (!bytes || fread(bytes, 1, *size, file) != *size) { free(bytes); fclose(file); return NULL; }
  fclose(file); return bytes;
}
typedef struct { IByteOut interface; FILE *file; size_t bytes; int failed; } Output;
static void write_byte(IByteOutPtr pointer, Byte value) {
  Output *out = (Output *)pointer;
  if (out->bytes >= (8 << 20) || fputc(value, out->file) == EOF) out->failed = 1;
  else out->bytes++;
}
static void *allocate(ISzAllocPtr allocator, size_t size) { (void)allocator; return malloc(size); }
static void release(ISzAllocPtr allocator, void *address) { (void)allocator; free(address); }

int main(int argc, char **argv) {
  Byte *bytes; size_t size; FILE *output; unsigned long first, second; int result = 0;
  if (argc != 7 || !number(argv[5], &first) || !number(argv[6], &second)) return 64;
  bytes = load(argv[3], &size); if (!bytes) return 65;
  output = fopen(argv[4], "wb"); if (!output) { free(bytes); return 66; }
  if (!strcmp(argv[1], "ppmd")) {
    CPpmd7 model; ISzAlloc allocator = { allocate, release }; Output out;
    if (first < 2 || first > 64 || second < (1 << 11) || second > (32 << 20)) result = 67;
    else {
      memset(&out, 0, sizeof(out)); out.interface.Write = write_byte; out.file = output;
      Ppmd7_Construct(&model);
      if (!Ppmd7_Alloc(&model, (UInt32)second, &allocator)) result = 68;
      else {
        Ppmd7_Init(&model, (unsigned)first); model.rc.enc.Stream = &out.interface;
        Ppmd7z_Init_RangeEnc(&model); Ppmd7z_EncodeSymbols(&model, bytes, bytes + size); Ppmd7z_Flush_RangeEnc(&model);
        Ppmd7_Free(&model, &allocator); if (out.failed) result = 69;
      }
    }
  } else if (!strcmp(argv[1], "filter")) {
    Byte *end = bytes; UInt32 state = Z7_BRANCH_CONV_ST_X86_STATE_INIT_VAL;
    if (!strcmp(argv[2], "X86")) end = Z7_BRANCH_CONV_ST_ENC(X86)(bytes, size, (UInt32)second, &state);
    else if (!strcmp(argv[2], "Arm")) end = Z7_BRANCH_CONV_ENC(ARM)(bytes, size, (UInt32)second);
    else if (!strcmp(argv[2], "ArmThumb")) end = Z7_BRANCH_CONV_ENC(ARMT)(bytes, size, (UInt32)second);
    else if (!strcmp(argv[2], "Arm64")) end = Z7_BRANCH_CONV_ENC(ARM64)(bytes, size, (UInt32)second);
    else if (!strcmp(argv[2], "Ppc")) end = Z7_BRANCH_CONV_ENC(PPC)(bytes, size, (UInt32)second);
    else if (!strcmp(argv[2], "Ia64")) end = Z7_BRANCH_CONV_ENC(IA64)(bytes, size, (UInt32)second);
    else if (!strcmp(argv[2], "Sparc")) end = Z7_BRANCH_CONV_ENC(SPARC)(bytes, size, (UInt32)second);
    else if (!strcmp(argv[2], "RiscV")) end = Z7_BRANCH_CONV_ENC(RISCV)(bytes, size, (UInt32)second);
    else if (!strcmp(argv[2], "Delta")) {
      Byte history[DELTA_STATE_SIZE];
      if (first < 1 || first > 256) result = 70;
      else { Delta_Init(history); Delta_Encode(history, (unsigned)first, bytes, size); end = bytes + size; }
    } else if (!strcmp(argv[2], "Swap2")) { z7_SwapBytesPrepare(); z7_SwapBytes2((UInt16 *)bytes, size / 2); end = bytes + (size & ~(size_t)1); }
    else if (!strcmp(argv[2], "Swap4")) { z7_SwapBytesPrepare(); z7_SwapBytes4((UInt32 *)bytes, size / 4); end = bytes + (size & ~(size_t)3); }
    else result = 71;
    if (!result && (end < bytes || end > bytes + size || fwrite(bytes, 1, size, output) != size)) result = 72;
  } else result = 73;
  if (fclose(output)) result = 74;
  free(bytes); return result;
}
