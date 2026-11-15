// The frame buffer Capri writes and the filter reads.
//
// Deliberately not OBS's queue and deliberately not OBS's CLSID. Registering our own DLL under OBS's class id would
// take over "OBS Virtual Camera" for every program on the machine, which would break the user's own OBS and VTube
// Studio setup. So this is a separate camera with its own name, and both can exist at once (Yuno, 14 Nov).
//
// One writer (Capri), many readers (whatever opened the camera). Three slots, written round robin, with a sequence
// number that the reader checks before and after copying. At 30fps a reader has about 66ms to copy a slot before the
// writer comes back to it, which is an eternity for a memcpy, so a torn frame is close to impossible and is detected
// rather than shown when it does happen.

#ifndef CAPRICAM_H
#define CAPRICAM_H

#include <windows.h>

#define CAPRICAM_SHARE   L"Local\\CapriVirtualCamera"
#define CAPRICAM_MAGIC   0x49525043u   /* 'CPRI' */
#define CAPRICAM_VERSION 1u
#define CAPRICAM_SLOTS   3

// Pixels are 32-bit BGRA, top row first. Capri draws in that already, so nothing is converted before it is shared.
#define CAPRICAM_BGRA32  1u

#pragma pack(push, 4)
typedef struct CapriCamHeader
{
    unsigned int magic;
    unsigned int version;
    unsigned int width;
    unsigned int height;
    unsigned int stride;      // bytes per row of a slot
    unsigned int format;      // CAPRICAM_BGRA32
    unsigned int slotBytes;   // stride * height
    unsigned int offsets[CAPRICAM_SLOTS];
    volatile unsigned int sequence;  // bumped after each finished frame
    volatile unsigned int newest;    // slot holding the latest finished frame
    volatile unsigned int living;    // 1 while Capri is sending
    unsigned __int64 written;        // 100ns units, when the newest frame was put there
    unsigned int reserved[8];
} CapriCamHeader;
#pragma pack(pop)

#endif
