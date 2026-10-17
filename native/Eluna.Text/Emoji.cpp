#include <windows.h>
#include <d2d1_1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <cstdint>

using Microsoft::WRL::ComPtr;

// Pixel buffers belong to the caller; no CRT allocations cross the C ABI.
extern "C" __declspec(dllexport) HRESULT RenderEmoji(
    const wchar_t* text, UINT32 length, UINT32 size, UINT32 stride, BYTE* pixels, UINT32 capacity) noexcept
{
    if (!text || !pixels || length == 0 || length > 64 || size < 8 || size > 512 || stride != size * 4 || capacity < stride * size)
        return E_INVALIDARG;
    const HRESULT apartment = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(apartment) && apartment != RPC_E_CHANGED_MODE) return apartment;
    HRESULT result = S_OK;
    {
        ComPtr<IWICImagingFactory> images;
        ComPtr<IWICBitmap> bitmap;
        ComPtr<ID2D1Factory> drawing;
        ComPtr<ID2D1RenderTarget> target;
        ComPtr<IDWriteFactory> writing;
        ComPtr<IDWriteTextFormat> format;
        ComPtr<IDWriteTextLayout> layout;
        ComPtr<ID2D1SolidColorBrush> brush;
        result = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&images));
        if (SUCCEEDED(result)) result = images->CreateBitmap(size, size, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, &bitmap);
        if (SUCCEEDED(result)) result = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, drawing.GetAddressOf());
        if (SUCCEEDED(result)) result = drawing->CreateWicBitmapRenderTarget(bitmap.Get(), D2D1::RenderTargetProperties(
            D2D1_RENDER_TARGET_TYPE_SOFTWARE, D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED), 96, 96), &target);
        if (SUCCEEDED(result)) result = DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(writing.GetAddressOf()));
        if (SUCCEEDED(result)) result = writing->CreateTextFormat(L"Segoe UI Emoji", nullptr, DWRITE_FONT_WEIGHT_NORMAL,
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, static_cast<FLOAT>(size) * 0.8f, L"ja-jp", &format);
        if (SUCCEEDED(result)) result = format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
        if (SUCCEEDED(result)) result = format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        if (SUCCEEDED(result)) result = format->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
        if (SUCCEEDED(result)) result = writing->CreateTextLayout(text, length, format.Get(), static_cast<FLOAT>(size), static_cast<FLOAT>(size), &layout);
        if (SUCCEEDED(result)) result = target->CreateSolidColorBrush(D2D1::ColorF(D2D1::ColorF::White), &brush);
        if (SUCCEEDED(result))
        {
            target->BeginDraw(); target->Clear(D2D1::ColorF(0, 0));
            target->DrawTextLayout(D2D1::Point2F(0, 0), layout.Get(), brush.Get(), D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT);
            result = target->EndDraw();
        }
        if (SUCCEEDED(result)) result = bitmap->CopyPixels(nullptr, stride, capacity, pixels);
    }
    if (SUCCEEDED(apartment)) CoUninitialize();
    return result;
}
