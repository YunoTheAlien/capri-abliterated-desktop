// Capri Virtual Camera: a DirectShow capture source so anything that takes a webcam takes her, with no OBS installed.
//
// Written against raw COM rather than the DirectShow base classes. The Windows 10 SDK still ships strmbase.lib but not
// its headers, so CSource and friends are not available without pulling in the old samples. Everything here is
// therefore hand rolled: the class factory, the filter, its one output pin, two enumerators and the push thread.
//
// What a capture device has to provide, and why each piece is here:
//   IBaseFilter + IMediaFilter + IPersist   the filter itself
//   IAMFilterMiscFlags                      says it is a source, so the graph knows who starts
//   IPin                                    connection, media type negotiation, flushing
//   IAMStreamConfig                         the sizes it offers; capture apps ask before connecting
//   IKsPropertySet                          says this pin is PIN_CATEGORY_CAPTURE, without which it is not a camera
//   IAMPushSource                           lets the graph know we push on our own clock
//
// Frames come from shared memory that Capri writes. See capricam.h.

#include <windows.h>
#include <dshow.h>
#include <initguid.h>
#include "capricam.h"

// Our own class id, nothing to do with OBS's.
// {332B1BDE-8136-4872-9BC3-E8E8CD85C5AA}
DEFINE_GUID(CLSID_CapriCam, 0x332b1bde, 0x8136, 0x4872, 0x9b, 0xc3, 0xe8, 0xe8, 0xcd, 0x85, 0xc5, 0xaa);

static HMODULE gModule = NULL;
static volatile LONG gObjects = 0;

static const int kWidth = 1280;
static const int kHeight = 720;
static const REFERENCE_TIME kFrame = 333333;   // 30fps in 100ns units

// The sizes offered. The first is the default; a capture app may pick any of them.
struct Offered { int wide; int tall; };
static const Offered kSizes[] = { { 1280, 720 }, { 640, 360 }, { 960, 540 }, { 1920, 1080 } };
static const int kSizeCount = sizeof(kSizes) / sizeof(kSizes[0]);

static void FreeType(AM_MEDIA_TYPE* mt)
{
    if (!mt) return;
    if (mt->cbFormat && mt->pbFormat) { CoTaskMemFree(mt->pbFormat); mt->pbFormat = NULL; mt->cbFormat = 0; }
    if (mt->pUnk) { mt->pUnk->Release(); mt->pUnk = NULL; }
}

static void DeleteType(AM_MEDIA_TYPE* mt)
{
    if (!mt) return;
    FreeType(mt);
    CoTaskMemFree(mt);
}

static BOOL FillType(AM_MEDIA_TYPE* mt, int wide, int tall)
{
    VIDEOINFOHEADER* vih = (VIDEOINFOHEADER*)CoTaskMemAlloc(sizeof(VIDEOINFOHEADER));
    if (!vih) return FALSE;
    ZeroMemory(vih, sizeof(VIDEOINFOHEADER));
    vih->AvgTimePerFrame = kFrame;
    vih->bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    vih->bmiHeader.biWidth = wide;
    // Positive height: a bottom-up DIB, which is what RGB24 means to DirectShow.
    vih->bmiHeader.biHeight = tall;
    vih->bmiHeader.biPlanes = 1;
    vih->bmiHeader.biBitCount = 24;
    vih->bmiHeader.biCompression = BI_RGB;
    vih->bmiHeader.biSizeImage = wide * tall * 3;
    vih->dwBitRate = (DWORD)((LONGLONG)vih->bmiHeader.biSizeImage * 8 * 10000000 / kFrame);
    SetRect(&vih->rcSource, 0, 0, wide, tall);
    SetRect(&vih->rcTarget, 0, 0, wide, tall);

    ZeroMemory(mt, sizeof(AM_MEDIA_TYPE));
    mt->majortype = MEDIATYPE_Video;
    mt->subtype = MEDIASUBTYPE_RGB24;
    mt->formattype = FORMAT_VideoInfo;
    mt->bFixedSizeSamples = TRUE;
    mt->bTemporalCompression = FALSE;
    mt->lSampleSize = wide * tall * 3;
    mt->cbFormat = sizeof(VIDEOINFOHEADER);
    mt->pbFormat = (BYTE*)vih;
    return TRUE;
}

static BOOL CopyType(AM_MEDIA_TYPE* to, const AM_MEDIA_TYPE* from)
{
    *to = *from;
    if (from->cbFormat && from->pbFormat)
    {
        to->pbFormat = (BYTE*)CoTaskMemAlloc(from->cbFormat);
        if (!to->pbFormat) { to->cbFormat = 0; return FALSE; }
        CopyMemory(to->pbFormat, from->pbFormat, from->cbFormat);
    }
    else { to->pbFormat = NULL; to->cbFormat = 0; }
    if (to->pUnk) to->pUnk->AddRef();
    return TRUE;
}

// Only RGB24 video in a VIDEOINFOHEADER, at a sane size.
static BOOL LikeType(const AM_MEDIA_TYPE* mt)
{
    if (!mt || mt->majortype != MEDIATYPE_Video) return FALSE;
    if (mt->subtype != MEDIASUBTYPE_RGB24) return FALSE;
    if (mt->formattype != FORMAT_VideoInfo || !mt->pbFormat || mt->cbFormat < sizeof(VIDEOINFOHEADER)) return FALSE;
    const VIDEOINFOHEADER* vih = (const VIDEOINFOHEADER*)mt->pbFormat;
    if (vih->bmiHeader.biBitCount != 24 || vih->bmiHeader.biCompression != BI_RGB) return FALSE;
    LONG wide = vih->bmiHeader.biWidth, tall = vih->bmiHeader.biHeight;
    if (tall < 0) tall = -tall;
    return wide >= 32 && wide <= 4096 && tall >= 32 && tall <= 4096;
}

class CapriFilter;

// ---------------------------------------------------------------- enumerators

class PinEnum : public IEnumPins
{
public:
    PinEnum(IPin* pin, ULONG at) : refs(1), only(pin), spot(at) { only->AddRef(); InterlockedIncrement(&gObjects); }
    ~PinEnum() { only->Release(); InterlockedDecrement(&gObjects); }

    STDMETHODIMP QueryInterface(REFIID id, void** out)
    {
        if (!out) return E_POINTER;
        if (id == IID_IUnknown || id == IID_IEnumPins) { *out = static_cast<IEnumPins*>(this); AddRef(); return S_OK; }
        *out = NULL; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&refs); }
    STDMETHODIMP_(ULONG) Release() { LONG n = InterlockedDecrement(&refs); if (!n) delete this; return n; }

    STDMETHODIMP Next(ULONG want, IPin** out, ULONG* got)
    {
        if (!out) return E_POINTER;
        ULONG made = 0;
        while (made < want && spot < 1) { only->AddRef(); out[made++] = only; spot++; }
        if (got) *got = made;
        return made == want ? S_OK : S_FALSE;
    }
    STDMETHODIMP Skip(ULONG many) { spot += many; return spot > 1 ? S_FALSE : S_OK; }
    STDMETHODIMP Reset() { spot = 0; return S_OK; }
    STDMETHODIMP Clone(IEnumPins** out)
    {
        if (!out) return E_POINTER;
        *out = new PinEnum(only, spot);
        return *out ? S_OK : E_OUTOFMEMORY;
    }
private:
    volatile LONG refs;
    IPin* only;
    ULONG spot;
};

class TypeEnum : public IEnumMediaTypes
{
public:
    TypeEnum(int wide, int tall, ULONG at) : refs(1), w(wide), h(tall), spot(at) { InterlockedIncrement(&gObjects); }
    ~TypeEnum() { InterlockedDecrement(&gObjects); }

    STDMETHODIMP QueryInterface(REFIID id, void** out)
    {
        if (!out) return E_POINTER;
        if (id == IID_IUnknown || id == IID_IEnumMediaTypes) { *out = static_cast<IEnumMediaTypes*>(this); AddRef(); return S_OK; }
        *out = NULL; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&refs); }
    STDMETHODIMP_(ULONG) Release() { LONG n = InterlockedDecrement(&refs); if (!n) delete this; return n; }

    STDMETHODIMP Next(ULONG want, AM_MEDIA_TYPE** out, ULONG* got)
    {
        if (!out) return E_POINTER;
        ULONG made = 0;
        while (made < want && spot < 1)
        {
            AM_MEDIA_TYPE* mt = (AM_MEDIA_TYPE*)CoTaskMemAlloc(sizeof(AM_MEDIA_TYPE));
            if (!mt) break;
            if (!FillType(mt, w, h)) { CoTaskMemFree(mt); break; }
            out[made++] = mt;
            spot++;
        }
        if (got) *got = made;
        return made == want ? S_OK : S_FALSE;
    }
    STDMETHODIMP Skip(ULONG many) { spot += many; return spot > 1 ? S_FALSE : S_OK; }
    STDMETHODIMP Reset() { spot = 0; return S_OK; }
    STDMETHODIMP Clone(IEnumMediaTypes** out)
    {
        if (!out) return E_POINTER;
        *out = new TypeEnum(w, h, spot);
        return *out ? S_OK : E_OUTOFMEMORY;
    }
private:
    volatile LONG refs;
    int w, h;
    ULONG spot;
};

// ---------------------------------------------------------------- the pin

class CapriPin : public IPin, public IAMStreamConfig, public IKsPropertySet, public IQualityControl, public IAMPushSource
{
public:
    CapriPin(CapriFilter* owner);
    ~CapriPin();

    // Called by the filter.
    HRESULT Begin();
    void End();
    void Owned(CapriFilter* owner) { filter = owner; }

    STDMETHODIMP QueryInterface(REFIID id, void** out);
    STDMETHODIMP_(ULONG) AddRef();
    STDMETHODIMP_(ULONG) Release();

    // IPin
    STDMETHODIMP Connect(IPin* other, const AM_MEDIA_TYPE* mt);
    STDMETHODIMP ReceiveConnection(IPin*, const AM_MEDIA_TYPE*) { return E_UNEXPECTED; }
    STDMETHODIMP Disconnect();
    STDMETHODIMP ConnectedTo(IPin** out);
    STDMETHODIMP ConnectionMediaType(AM_MEDIA_TYPE* out);
    STDMETHODIMP QueryPinInfo(PIN_INFO* out);
    STDMETHODIMP QueryDirection(PIN_DIRECTION* out) { if (!out) return E_POINTER; *out = PINDIR_OUTPUT; return S_OK; }
    STDMETHODIMP QueryId(LPWSTR* out);
    STDMETHODIMP QueryAccept(const AM_MEDIA_TYPE* mt) { return LikeType(mt) ? S_OK : S_FALSE; }
    STDMETHODIMP EnumMediaTypes(IEnumMediaTypes** out);
    STDMETHODIMP QueryInternalConnections(IPin**, ULONG* many) { if (many) *many = 0; return S_OK; }
    STDMETHODIMP EndOfStream() { return S_OK; }
    STDMETHODIMP BeginFlush() { return S_OK; }
    STDMETHODIMP EndFlush() { return S_OK; }
    STDMETHODIMP NewSegment(REFERENCE_TIME, REFERENCE_TIME, double) { return S_OK; }

    // IAMStreamConfig
    STDMETHODIMP SetFormat(AM_MEDIA_TYPE* mt);
    STDMETHODIMP GetFormat(AM_MEDIA_TYPE** out);
    STDMETHODIMP GetNumberOfCapabilities(int* count, int* size);
    STDMETHODIMP GetStreamCaps(int which, AM_MEDIA_TYPE** out, BYTE* caps);

    // IKsPropertySet: the one that makes it a camera rather than just a filter.
    STDMETHODIMP Set(REFGUID, DWORD, LPVOID, DWORD, LPVOID, DWORD) { return E_NOTIMPL; }
    STDMETHODIMP Get(REFGUID set, DWORD id, LPVOID, DWORD, LPVOID out, DWORD room, DWORD* given);
    STDMETHODIMP QuerySupported(REFGUID set, DWORD id, DWORD* kind);

    // IQualityControl
    STDMETHODIMP Notify(IBaseFilter*, Quality) { return S_OK; }
    STDMETHODIMP SetSink(IQualityControl*) { return S_OK; }

    // IAMPushSource
    STDMETHODIMP GetPushSourceFlags(ULONG* flags) { if (!flags) return E_POINTER; *flags = 0; return S_OK; }
    STDMETHODIMP SetPushSourceFlags(ULONG) { return S_OK; }
    STDMETHODIMP SetStreamOffset(REFERENCE_TIME) { return S_OK; }
    STDMETHODIMP GetStreamOffset(REFERENCE_TIME* at) { if (!at) return E_POINTER; *at = 0; return S_OK; }
    STDMETHODIMP GetMaxStreamOffset(REFERENCE_TIME* at) { if (!at) return E_POINTER; *at = 0; return S_OK; }
    STDMETHODIMP SetMaxStreamOffset(REFERENCE_TIME) { return S_OK; }
    STDMETHODIMP GetLatency(REFERENCE_TIME* out) { if (!out) return E_POINTER; *out = kFrame; return S_OK; }

private:
    static DWORD WINAPI Pump(LPVOID me) { ((CapriPin*)me)->Push(); return 0; }
    void Push();
    void Paint(BYTE* into, long room);

    volatile LONG refs;
    CapriFilter* filter;
    IPin* joined;
    IMemInputPin* feed;
    IMemAllocator* pool;
    AM_MEDIA_TYPE agreed;
    BOOL hasAgreed;
    int wide, tall;

    HANDLE thread;
    HANDLE stop;
    CRITICAL_SECTION lock;

    // The shared frame buffer, opened lazily and reopened if Capri restarts.
    HANDLE share;
    CapriCamHeader* head;
    BYTE* pixels;
    DWORD lookedAt;
    void OpenShare();
    void ShutShare();
};

// ---------------------------------------------------------------- the filter

class CapriFilter : public IBaseFilter, public IAMFilterMiscFlags
{
public:
    CapriFilter() : refs(1), state(State_Stopped), graph(NULL), sink(NULL), clock(NULL), start(0)
    {
        InterlockedIncrement(&gObjects);
        InitializeCriticalSection(&lock);
        pin = new CapriPin(this);
        name[0] = 0;
    }
    ~CapriFilter()
    {
        if (pin) { pin->Owned(NULL); pin->Release(); pin = NULL; }
        if (clock) clock->Release();
        DeleteCriticalSection(&lock);
        InterlockedDecrement(&gObjects);
    }

    STDMETHODIMP QueryInterface(REFIID id, void** out)
    {
        if (!out) return E_POINTER;
        if (id == IID_IUnknown || id == IID_IBaseFilter || id == IID_IMediaFilter || id == IID_IPersist)
            { *out = static_cast<IBaseFilter*>(this); AddRef(); return S_OK; }
        if (id == IID_IAMFilterMiscFlags) { *out = static_cast<IAMFilterMiscFlags*>(this); AddRef(); return S_OK; }
        *out = NULL; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&refs); }
    STDMETHODIMP_(ULONG) Release() { LONG n = InterlockedDecrement(&refs); if (!n) delete this; return n; }

    // IPersist
    STDMETHODIMP GetClassID(CLSID* out) { if (!out) return E_POINTER; *out = CLSID_CapriCam; return S_OK; }

    // IMediaFilter
    STDMETHODIMP Stop()
    {
        EnterCriticalSection(&lock);
        state = State_Stopped;
        LeaveCriticalSection(&lock);
        if (pin) pin->End();
        return S_OK;
    }
    STDMETHODIMP Pause()
    {
        EnterCriticalSection(&lock);
        state = State_Paused;
        LeaveCriticalSection(&lock);
        // A live source keeps producing while paused, so the graph has something to show.
        if (pin) pin->Begin();
        return S_OK;
    }
    STDMETHODIMP Run(REFERENCE_TIME at)
    {
        EnterCriticalSection(&lock);
        start = at;
        state = State_Running;
        LeaveCriticalSection(&lock);
        if (pin) pin->Begin();
        return S_OK;
    }
    STDMETHODIMP GetState(DWORD, FILTER_STATE* out) { if (!out) return E_POINTER; *out = state; return S_OK; }
    STDMETHODIMP SetSyncSource(IReferenceClock* which)
    {
        EnterCriticalSection(&lock);
        if (clock) clock->Release();
        clock = which;
        if (clock) clock->AddRef();
        LeaveCriticalSection(&lock);
        return S_OK;
    }
    STDMETHODIMP GetSyncSource(IReferenceClock** out)
    {
        if (!out) return E_POINTER;
        EnterCriticalSection(&lock);
        *out = clock;
        if (*out) (*out)->AddRef();
        LeaveCriticalSection(&lock);
        return S_OK;
    }

    // IBaseFilter
    STDMETHODIMP EnumPins(IEnumPins** out)
    {
        if (!out) return E_POINTER;
        *out = new PinEnum(static_cast<IPin*>(pin), 0);
        return *out ? S_OK : E_OUTOFMEMORY;
    }
    STDMETHODIMP FindPin(LPCWSTR id, IPin** out)
    {
        if (!out) return E_POINTER;
        *out = NULL;
        if (id && lstrcmpW(id, L"Capture") == 0) { *out = static_cast<IPin*>(pin); (*out)->AddRef(); return S_OK; }
        return VFW_E_NOT_FOUND;
    }
    STDMETHODIMP QueryFilterInfo(FILTER_INFO* out)
    {
        if (!out) return E_POINTER;
        lstrcpynW(out->achName, name[0] ? name : L"Capri Virtual Camera", MAX_FILTER_NAME);
        out->pGraph = graph;
        if (out->pGraph) out->pGraph->AddRef();
        return S_OK;
    }
    STDMETHODIMP JoinFilterGraph(IFilterGraph* which, LPCWSTR named)
    {
        // Deliberately not AddRef'd: the graph owns the filter, and holding a reference back would be a cycle neither
        // side could break.
        graph = which;
        if (named) lstrcpynW(name, named, MAX_FILTER_NAME); else name[0] = 0;
        return S_OK;
    }
    STDMETHODIMP QueryVendorInfo(LPWSTR* out) { if (out) *out = NULL; return E_NOTIMPL; }

    // IAMFilterMiscFlags
    STDMETHODIMP_(ULONG) GetMiscFlags() { return AM_FILTER_MISC_FLAGS_IS_SOURCE; }

    FILTER_STATE State() { return state; }
    REFERENCE_TIME Started() { return start; }
    IReferenceClock* Clock() { return clock; }

private:
    volatile LONG refs;
    FILTER_STATE state;
    IFilterGraph* graph;
    IUnknown* sink;
    IReferenceClock* clock;
    REFERENCE_TIME start;
    CapriPin* pin;
    WCHAR name[MAX_FILTER_NAME];
    CRITICAL_SECTION lock;
};

// ---------------------------------------------------------------- pin body

CapriPin::CapriPin(CapriFilter* owner)
    : refs(1), filter(owner), joined(NULL), feed(NULL), pool(NULL), hasAgreed(FALSE),
      wide(kWidth), tall(kHeight), thread(NULL), stop(NULL), share(NULL), head(NULL), pixels(NULL), lookedAt(0)
{
    InterlockedIncrement(&gObjects);
    InitializeCriticalSection(&lock);
    ZeroMemory(&agreed, sizeof(agreed));
}

CapriPin::~CapriPin()
{
    End();
    ShutShare();
    if (hasAgreed) FreeType(&agreed);
    if (pool) pool->Release();
    if (feed) feed->Release();
    if (joined) joined->Release();
    DeleteCriticalSection(&lock);
    InterlockedDecrement(&gObjects);
}

STDMETHODIMP CapriPin::QueryInterface(REFIID id, void** out)
{
    if (!out) return E_POINTER;
    if (id == IID_IUnknown || id == IID_IPin) { *out = static_cast<IPin*>(this); AddRef(); return S_OK; }
    if (id == IID_IAMStreamConfig) { *out = static_cast<IAMStreamConfig*>(this); AddRef(); return S_OK; }
    if (id == IID_IKsPropertySet) { *out = static_cast<IKsPropertySet*>(this); AddRef(); return S_OK; }
    if (id == IID_IQualityControl) { *out = static_cast<IQualityControl*>(this); AddRef(); return S_OK; }
    if (id == IID_IAMPushSource || id == IID_IAMLatency) { *out = static_cast<IAMPushSource*>(this); AddRef(); return S_OK; }
    *out = NULL; return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG) CapriPin::AddRef() { return InterlockedIncrement(&refs); }
STDMETHODIMP_(ULONG) CapriPin::Release() { LONG n = InterlockedDecrement(&refs); if (!n) delete this; return n; }

STDMETHODIMP CapriPin::QueryId(LPWSTR* out)
{
    if (!out) return E_POINTER;
    const WCHAR* id = L"Capture";
    size_t bytes = (lstrlenW(id) + 1) * sizeof(WCHAR);
    *out = (LPWSTR)CoTaskMemAlloc(bytes);
    if (!*out) return E_OUTOFMEMORY;
    CopyMemory(*out, id, bytes);
    return S_OK;
}

STDMETHODIMP CapriPin::QueryPinInfo(PIN_INFO* out)
{
    if (!out) return E_POINTER;
    out->pFilter = filter;
    if (out->pFilter) out->pFilter->AddRef();
    out->dir = PINDIR_OUTPUT;
    lstrcpynW(out->achName, L"Capture", MAX_PIN_NAME);
    return S_OK;
}

STDMETHODIMP CapriPin::EnumMediaTypes(IEnumMediaTypes** out)
{
    if (!out) return E_POINTER;
    *out = new TypeEnum(wide, tall, 0);
    return *out ? S_OK : E_OUTOFMEMORY;
}

STDMETHODIMP CapriPin::ConnectedTo(IPin** out)
{
    if (!out) return E_POINTER;
    EnterCriticalSection(&lock);
    *out = joined;
    if (*out) (*out)->AddRef();
    LeaveCriticalSection(&lock);
    return *out ? S_OK : VFW_E_NOT_CONNECTED;
}

STDMETHODIMP CapriPin::ConnectionMediaType(AM_MEDIA_TYPE* out)
{
    if (!out) return E_POINTER;
    EnterCriticalSection(&lock);
    HRESULT hr = hasAgreed && CopyType(out, &agreed) ? S_OK : VFW_E_NOT_CONNECTED;
    LeaveCriticalSection(&lock);
    return hr;
}

STDMETHODIMP CapriPin::Connect(IPin* other, const AM_MEDIA_TYPE* want)
{
    if (!other) return E_POINTER;
    if (joined) return VFW_E_ALREADY_CONNECTED;
    if (filter && filter->State() != State_Stopped) return VFW_E_NOT_STOPPED;

    AM_MEDIA_TYPE mt;
    ZeroMemory(&mt, sizeof(mt));
    if (want && want->majortype == MEDIATYPE_Video && want->pbFormat)
    {
        if (!LikeType(want)) return VFW_E_TYPE_NOT_ACCEPTED;
        if (!CopyType(&mt, want)) return E_OUTOFMEMORY;
    }
    else if (!FillType(&mt, wide, tall)) return E_OUTOFMEMORY;

    HRESULT hr = other->ReceiveConnection(static_cast<IPin*>(this), &mt);
    if (FAILED(hr)) { FreeType(&mt); return hr; }

    IMemInputPin* input = NULL;
    hr = other->QueryInterface(IID_IMemInputPin, (void**)&input);
    if (FAILED(hr)) { other->Disconnect(); FreeType(&mt); return hr; }

    // Their allocator if they have one, ours otherwise. Either way we commit it, since a source pin drives.
    IMemAllocator* mine = NULL;
    hr = input->GetAllocator(&mine);
    if (FAILED(hr) || !mine)
    {
        hr = CoCreateInstance(CLSID_MemoryAllocator, NULL, CLSCTX_INPROC_SERVER, IID_IMemAllocator, (void**)&mine);
        if (FAILED(hr)) { input->Release(); other->Disconnect(); FreeType(&mt); return hr; }
    }

    const VIDEOINFOHEADER* vih = (const VIDEOINFOHEADER*)mt.pbFormat;
    LONG tallest = vih->bmiHeader.biHeight < 0 ? -vih->bmiHeader.biHeight : vih->bmiHeader.biHeight;

    ALLOCATOR_PROPERTIES asked, given;
    ZeroMemory(&asked, sizeof(asked));
    ZeroMemory(&given, sizeof(given));
    asked.cBuffers = 3;
    asked.cbBuffer = vih->bmiHeader.biWidth * tallest * 3;
    asked.cbAlign = 1;
    hr = mine->SetProperties(&asked, &given);
    if (SUCCEEDED(hr) && given.cbBuffer < asked.cbBuffer) hr = VFW_E_SIZENOTSET;
    if (SUCCEEDED(hr)) hr = input->NotifyAllocator(mine, FALSE);
    if (SUCCEEDED(hr)) hr = mine->Commit();
    if (FAILED(hr)) { mine->Release(); input->Release(); other->Disconnect(); FreeType(&mt); return hr; }

    EnterCriticalSection(&lock);
    if (hasAgreed) FreeType(&agreed);
    agreed = mt;
    hasAgreed = TRUE;
    wide = vih->bmiHeader.biWidth;
    tall = tallest;
    joined = other;
    joined->AddRef();
    feed = input;
    pool = mine;
    LeaveCriticalSection(&lock);
    return S_OK;
}

STDMETHODIMP CapriPin::Disconnect()
{
    if (filter && filter->State() != State_Stopped) return VFW_E_NOT_STOPPED;
    End();
    EnterCriticalSection(&lock);
    if (pool) { pool->Decommit(); pool->Release(); pool = NULL; }
    if (feed) { feed->Release(); feed = NULL; }
    if (joined) { joined->Release(); joined = NULL; }
    if (hasAgreed) { FreeType(&agreed); hasAgreed = FALSE; }
    LeaveCriticalSection(&lock);
    return S_OK;
}

STDMETHODIMP CapriPin::SetFormat(AM_MEDIA_TYPE* mt)
{
    if (!mt) return E_POINTER;
    if (!LikeType(mt)) return VFW_E_INVALIDMEDIATYPE;
    if (joined) return VFW_E_ALREADY_CONNECTED;
    const VIDEOINFOHEADER* vih = (const VIDEOINFOHEADER*)mt->pbFormat;
    EnterCriticalSection(&lock);
    wide = vih->bmiHeader.biWidth;
    tall = vih->bmiHeader.biHeight < 0 ? -vih->bmiHeader.biHeight : vih->bmiHeader.biHeight;
    LeaveCriticalSection(&lock);
    return S_OK;
}

STDMETHODIMP CapriPin::GetFormat(AM_MEDIA_TYPE** out)
{
    if (!out) return E_POINTER;
    AM_MEDIA_TYPE* mt = (AM_MEDIA_TYPE*)CoTaskMemAlloc(sizeof(AM_MEDIA_TYPE));
    if (!mt) return E_OUTOFMEMORY;
    EnterCriticalSection(&lock);
    BOOL ok = hasAgreed ? CopyType(mt, &agreed) : FillType(mt, wide, tall);
    LeaveCriticalSection(&lock);
    if (!ok) { CoTaskMemFree(mt); return E_OUTOFMEMORY; }
    *out = mt;
    return S_OK;
}

STDMETHODIMP CapriPin::GetNumberOfCapabilities(int* count, int* size)
{
    if (!count || !size) return E_POINTER;
    *count = kSizeCount;
    *size = sizeof(VIDEO_STREAM_CONFIG_CAPS);
    return S_OK;
}

STDMETHODIMP CapriPin::GetStreamCaps(int which, AM_MEDIA_TYPE** out, BYTE* caps)
{
    if (!out || !caps) return E_POINTER;
    if (which < 0 || which >= kSizeCount) return S_FALSE;

    AM_MEDIA_TYPE* mt = (AM_MEDIA_TYPE*)CoTaskMemAlloc(sizeof(AM_MEDIA_TYPE));
    if (!mt) return E_OUTOFMEMORY;
    if (!FillType(mt, kSizes[which].wide, kSizes[which].tall)) { CoTaskMemFree(mt); return E_OUTOFMEMORY; }
    *out = mt;

    VIDEO_STREAM_CONFIG_CAPS* able = (VIDEO_STREAM_CONFIG_CAPS*)caps;
    ZeroMemory(able, sizeof(VIDEO_STREAM_CONFIG_CAPS));
    able->guid = FORMAT_VideoInfo;
    able->VideoStandard = AnalogVideo_None;
    able->InputSize.cx = kSizes[which].wide;
    able->InputSize.cy = kSizes[which].tall;
    able->MinCroppingSize = able->InputSize;
    able->MaxCroppingSize = able->InputSize;
    able->CropGranularityX = 1;
    able->CropGranularityY = 1;
    able->MinOutputSize = able->InputSize;
    able->MaxOutputSize = able->InputSize;
    able->OutputGranularityX = 1;
    able->OutputGranularityY = 1;
    able->MinFrameInterval = kFrame;
    able->MaxFrameInterval = 10000000;
    able->MinBitsPerSecond = able->MaxBitsPerSecond =
        (LONG)((LONGLONG)kSizes[which].wide * kSizes[which].tall * 3 * 8 * 10000000 / kFrame);
    return S_OK;
}

STDMETHODIMP CapriPin::Get(REFGUID set, DWORD id, LPVOID, DWORD, LPVOID out, DWORD room, DWORD* given)
{
    if (set != AMPROPSETID_Pin) return E_PROP_SET_UNSUPPORTED;
    if (id != AMPROPERTY_PIN_CATEGORY) return E_PROP_ID_UNSUPPORTED;
    if (given) *given = sizeof(GUID);
    if (!out) return S_OK;
    if (room < sizeof(GUID)) return E_UNEXPECTED;
    *(GUID*)out = PIN_CATEGORY_CAPTURE;
    return S_OK;
}

STDMETHODIMP CapriPin::QuerySupported(REFGUID set, DWORD id, DWORD* kind)
{
    if (set != AMPROPSETID_Pin) return E_PROP_SET_UNSUPPORTED;
    if (id != AMPROPERTY_PIN_CATEGORY) return E_PROP_ID_UNSUPPORTED;
    if (kind) *kind = KSPROPERTY_SUPPORT_GET;
    return S_OK;
}

HRESULT CapriPin::Begin()
{
    EnterCriticalSection(&lock);
    HRESULT hr = S_OK;
    if (!joined || !feed) hr = VFW_E_NOT_CONNECTED;
    else if (!thread)
    {
        stop = CreateEventW(NULL, TRUE, FALSE, NULL);
        if (!stop) hr = E_OUTOFMEMORY;
        else
        {
            thread = CreateThread(NULL, 0, Pump, this, 0, NULL);
            if (!thread) { CloseHandle(stop); stop = NULL; hr = E_FAIL; }
        }
    }
    LeaveCriticalSection(&lock);
    return hr;
}

void CapriPin::End()
{
    HANDLE ending = NULL, mark = NULL;
    EnterCriticalSection(&lock);
    ending = thread; thread = NULL;
    mark = stop; stop = NULL;
    LeaveCriticalSection(&lock);
    if (mark) SetEvent(mark);
    if (ending) { WaitForSingleObject(ending, 3000); CloseHandle(ending); }
    if (mark) CloseHandle(mark);
}

void CapriPin::OpenShare()
{
    if (head) return;
    share = OpenFileMappingW(FILE_MAP_READ, FALSE, CAPRICAM_SHARE);
    if (!share) return;
    head = (CapriCamHeader*)MapViewOfFile(share, FILE_MAP_READ, 0, 0, 0);
    if (!head) { CloseHandle(share); share = NULL; return; }
    if (head->magic != CAPRICAM_MAGIC || head->version != CAPRICAM_VERSION) { ShutShare(); return; }
    pixels = (BYTE*)head;
}

void CapriPin::ShutShare()
{
    if (head) UnmapViewOfFile(head);
    if (share) CloseHandle(share);
    head = NULL;
    share = NULL;
    pixels = NULL;
}

// Fills one RGB24 bottom-up frame. Capri's picture when there is one, her colour when there is not, so the camera is
// never a black rectangle that looks broken.
void CapriPin::Paint(BYTE* into, long room)
{
    long need = (long)wide * tall * 3;
    if (room < need) return;

    OpenShare();
    BOOL drew = FALSE;

    if (head && head->living && head->width && head->height)
    {
        unsigned int before = head->sequence;
        unsigned int slot = head->newest;
        if (slot < CAPRICAM_SLOTS)
        {
            unsigned int sw = head->width, sh = head->height, stride = head->stride;
            unsigned int at = head->offsets[slot];
            const BYTE* from = pixels + at;
            // Nearest neighbour: the source is normally the same size, and a camera that is one frame late looks far
            // worse than one that is scaled roughly.
            for (int y = 0; y < tall; y++)
            {
                // Bottom-up output.
                BYTE* row = into + (size_t)(tall - 1 - y) * wide * 3;
                unsigned int sy = sh == (unsigned)tall ? (unsigned)y : (unsigned int)((unsigned __int64)y * sh / tall);
                if (sy >= sh) sy = sh - 1;
                const BYTE* src = from + (size_t)sy * stride;
                for (int x = 0; x < wide; x++)
                {
                    unsigned int sx = sw == (unsigned)wide ? (unsigned)x : (unsigned int)((unsigned __int64)x * sw / wide);
                    if (sx >= sw) sx = sw - 1;
                    const BYTE* p = src + (size_t)sx * 4;
                    row[x * 3 + 0] = p[0];
                    row[x * 3 + 1] = p[1];
                    row[x * 3 + 2] = p[2];
                }
            }
            if (head->sequence == before || head->sequence == before + 1) drew = TRUE;
        }
    }
    else if (head && !head->living)
    {
        // Capri closed. Drop the mapping so a later start is picked up.
        ShutShare();
    }

    if (!drew)
    {
        // Her green, so an unconfigured capture still keys cleanly.
        for (long at = 0; at < need; at += 3) { into[at] = 64; into[at + 1] = 177; into[at + 2] = 0; }
    }
}

void CapriPin::Push()
{
    IMemInputPin* input = NULL;
    IMemAllocator* from = NULL;
    EnterCriticalSection(&lock);
    input = feed; if (input) input->AddRef();
    from = pool; if (from) from->AddRef();
    LeaveCriticalSection(&lock);
    if (!input || !from) { if (input) input->Release(); if (from) from->Release(); return; }

    REFERENCE_TIME at = 0;
    LARGE_INTEGER tick, rate;
    QueryPerformanceFrequency(&rate);
    QueryPerformanceCounter(&tick);
    LONGLONG began = tick.QuadPart;
    LONGLONG count = 0;

    for (;;)
    {
        HANDLE mark;
        EnterCriticalSection(&lock);
        mark = stop;
        LeaveCriticalSection(&lock);
        if (!mark || WaitForSingleObject(mark, 0) == WAIT_OBJECT_0) break;

        IMediaSample* sample = NULL;
        if (FAILED(from->GetBuffer(&sample, NULL, NULL, 0)) || !sample) { Sleep(5); continue; }

        BYTE* into = NULL;
        if (SUCCEEDED(sample->GetPointer(&into)) && into)
        {
            EnterCriticalSection(&lock);
            Paint(into, sample->GetSize());
            long used = (long)wide * tall * 3;
            LeaveCriticalSection(&lock);
            sample->SetActualDataLength(used);
            REFERENCE_TIME to = at + kFrame;
            sample->SetTime(&at, &to);
            sample->SetSyncPoint(TRUE);
            sample->SetDiscontinuity(count == 0);
            at = to;
        }

        HRESULT hr = input->Receive(sample);
        sample->Release();
        if (FAILED(hr)) break;

        // Paced off the performance counter rather than Sleep alone, so the frame rate does not drift.
        count++;
        LONGLONG due = began + (LONGLONG)((double)count * rate.QuadPart * kFrame / 10000000.0);
        QueryPerformanceCounter(&tick);
        LONGLONG wait = (due - tick.QuadPart) * 1000 / rate.QuadPart;
        if (wait > 0) Sleep((DWORD)(wait > 200 ? 200 : wait));
        else if (wait < -rate.QuadPart) { began = tick.QuadPart; count = 0; }
    }

    input->Release();
    from->Release();
}

// ---------------------------------------------------------------- class factory

class Factory : public IClassFactory
{
public:
    Factory() : refs(1) { InterlockedIncrement(&gObjects); }
    ~Factory() { InterlockedDecrement(&gObjects); }

    STDMETHODIMP QueryInterface(REFIID id, void** out)
    {
        if (!out) return E_POINTER;
        if (id == IID_IUnknown || id == IID_IClassFactory) { *out = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        *out = NULL; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&refs); }
    STDMETHODIMP_(ULONG) Release() { LONG n = InterlockedDecrement(&refs); if (!n) delete this; return n; }

    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID id, void** out)
    {
        if (!out) return E_POINTER;
        *out = NULL;
        if (outer) return CLASS_E_NOAGGREGATION;
        CapriFilter* made = new CapriFilter();
        if (!made) return E_OUTOFMEMORY;
        HRESULT hr = made->QueryInterface(id, out);
        made->Release();
        return hr;
    }
    STDMETHODIMP LockServer(BOOL lock) { if (lock) InterlockedIncrement(&gObjects); else InterlockedDecrement(&gObjects); return S_OK; }
private:
    volatile LONG refs;
};

// ---------------------------------------------------------------- exports

STDAPI DllGetClassObject(REFCLSID id, REFIID want, void** out)
{
    if (!out) return E_POINTER;
    *out = NULL;
    if (id != CLSID_CapriCam) return CLASS_E_CLASSNOTAVAILABLE;
    Factory* made = new Factory();
    if (!made) return E_OUTOFMEMORY;
    HRESULT hr = made->QueryInterface(want, out);
    made->Release();
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return gObjects == 0 ? S_OK : S_FALSE;
}

// Registration is done by Capri in HKCU so it needs no administrator, but these are here so regsvr32 also works.
static LONG WriteKey(HKEY root, const WCHAR* path, const WCHAR* name, const WCHAR* value)
{
    HKEY key = NULL;
    LONG hr = RegCreateKeyExW(root, path, 0, NULL, 0, KEY_WRITE, NULL, &key, NULL);
    if (hr != ERROR_SUCCESS) return hr;
    hr = RegSetValueExW(key, name, 0, REG_SZ, (const BYTE*)value, (DWORD)((lstrlenW(value) + 1) * sizeof(WCHAR)));
    RegCloseKey(key);
    return hr;
}

STDAPI DllRegisterServer()
{
    WCHAR me[MAX_PATH];
    if (!GetModuleFileNameW(gModule, me, MAX_PATH)) return E_FAIL;

    const WCHAR* id = L"{332B1BDE-8136-4872-9BC3-E8E8CD85C5AA}";
    WCHAR path[512];

    wsprintfW(path, L"Software\\Classes\\CLSID\\%s", id);
    if (WriteKey(HKEY_CURRENT_USER, path, NULL, L"Capri Virtual Camera") != ERROR_SUCCESS) return E_ACCESSDENIED;

    wsprintfW(path, L"Software\\Classes\\CLSID\\%s\\InprocServer32", id);
    if (WriteKey(HKEY_CURRENT_USER, path, NULL, me) != ERROR_SUCCESS) return E_ACCESSDENIED;
    WriteKey(HKEY_CURRENT_USER, path, L"ThreadingModel", L"Both");

    wsprintfW(path, L"Software\\Classes\\CLSID\\{860BB310-5D01-11d0-BD3B-00A0C911CE86}\\Instance\\%s", id);
    WriteKey(HKEY_CURRENT_USER, path, L"FriendlyName", L"Capri Virtual Camera");
    WriteKey(HKEY_CURRENT_USER, path, L"CLSID", id);
    return S_OK;
}

STDAPI DllUnregisterServer()
{
    const WCHAR* id = L"{332B1BDE-8136-4872-9BC3-E8E8CD85C5AA}";
    WCHAR path[512];
    wsprintfW(path, L"Software\\Classes\\CLSID\\{860BB310-5D01-11d0-BD3B-00A0C911CE86}\\Instance\\%s", id);
    RegDeleteKeyW(HKEY_CURRENT_USER, path);
    wsprintfW(path, L"Software\\Classes\\CLSID\\%s\\InprocServer32", id);
    RegDeleteKeyW(HKEY_CURRENT_USER, path);
    wsprintfW(path, L"Software\\Classes\\CLSID\\%s", id);
    RegDeleteKeyW(HKEY_CURRENT_USER, path);
    return S_OK;
}

BOOL WINAPI DllMain(HINSTANCE me, DWORD why, LPVOID)
{
    if (why == DLL_PROCESS_ATTACH) { gModule = (HMODULE)me; DisableThreadLibraryCalls(me); }
    return TRUE;
}
