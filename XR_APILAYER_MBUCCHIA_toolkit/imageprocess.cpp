// MIT License
//
// Copyright(c) 2021 Matthieu Bucchianeri
// Copyright(c) 2021-2022 Jean-Luc Dupiot - Reality XP
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this softwareand associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and /or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright noticeand this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

#include "pch.h"

#include "shader_utilities.h"
#include "factories.h"
#include "interfaces.h"
#include "layer.h"
#include "log.h"

namespace {

    using namespace toolkit;
    using namespace toolkit::config;
    using namespace toolkit::graphics;
    using namespace toolkit::log;

    struct alignas(16) ImageProcessorConfig {
        XrVector4f Params1; // Contrast, Brightness, Exposure, Saturation (-1..+1 params)
        XrVector4f Params2; // ColorGainR, ColorGainG, ColorGainB (-1..+1 params), FakeHDR (0..1 param)
        XrVector4f Params3; // Highlights, Shadows, Vibrance (0..1 params), UseCA (0 = off, 1 = on)
        XrVector4f Params4; // ChromaticCorrectionR, ChromaticCorrectionG, ChromaticCorrectionB (-1..+1 params)
                            // Eye (0 = left, 1 = right)
        XrVector4f Params5; // ColorSpace matrix row 0 (linear RGB -> linear RGB), UseColorSpace (0 = off, 1 = on)
        XrVector4f Params6; // ColorSpace matrix row 1, DitherAmplitude (0 = off, else 1 / (2^bits - 1) of the output)
        XrVector4f Params7; // ColorSpace matrix row 2, OutputIsSRGB (0 = no, 1 = yes)
    };

    class ImageProcessor : public IImageProcessor {
      public:
        ImageProcessor(std::shared_ptr<IConfigManager> configManager, std::shared_ptr<IDevice> graphicsDevice)
            : m_configManager(configManager), m_device(graphicsDevice),
              m_userParams(GetParams(configManager.get(), 1)) {
            createRenderResources();
        }

        void reload() override {
            createRenderResources();
        }

        void update() override {
            // Generic implementation to support more than just Off/On modes in the future.
            const auto mode = m_configManager->getEnumValue<PostProcessType>(config::SettingPostProcess);
            const auto hasModeChanged = mode != m_mode;

            if (hasModeChanged)
                m_mode = mode;

            if (hasModeChanged || checkUpdateConfig(mode)) {
                updateConfig();
            }
        }

        void process(std::shared_ptr<ITexture> input,
                     std::shared_ptr<ITexture> output,
                     std::vector<std::shared_ptr<ITexture>>& textures,
                     std::array<uint8_t, 1024>& blob,
                     std::optional<utilities::Eye> eye = std::nullopt) override {
            // We need to use a per-instance blob.
            static_assert(sizeof(ImageProcessorConfig) <= 1024);
            ImageProcessorConfig* const config = reinterpret_cast<ImageProcessorConfig*>(blob.data());

            memcpy(config, &m_config, sizeof(m_config));

            // Patch the eye.
            config->Params4.w = (float)eye.value_or(utilities::Eye::Both);

            // Patch the dithering, which depends on the format of the output.
            const auto outputFormat = output->getInfo().format;
            config->Params6.w = m_useDither ? GetDitherAmplitude(outputFormat) : 0.0f;
            config->Params7.w = m_device->isTextureFormatSRGB(outputFormat) ? 1.0f : 0.0f;

            // TODO: We can use an IShaderBuffer cache per swapchain and avoid this every frame.
            m_cbParams->uploadData(config, sizeof(*config));

            const auto usePostProcess = m_mode == PostProcessType::On;
            m_device->setShader(m_shaders[usePostProcess], SamplerType::LinearClamp);
            m_device->setShaderInput(0, m_cbParams);
            m_device->setShaderInput(0, input);
            m_device->setShaderOutput(0, output);
            m_device->dispatchShader();
        }

      private:
        void createRenderResources() {
            const auto shadersDir = dllHome / "shaders";
            const auto shaderFile = shadersDir / "postprocess.hlsl";

            utilities::shader::Defines defines;
            // defines.add("POST_PROCESS_SRC_SRGB", true);
            // defines.add("POST_PROCESS_DST_SRGB", true);

            defines.add("PASS_THROUGH_USE_GAINS", true);
            m_shaders[0] = m_device->createQuadShader(shaderFile, "mainPassThrough", "Passthrough PS", defines.get());
            m_shaders[1] = m_device->createQuadShader(shaderFile, "mainPostProcess", "Postprocess PS", defines.get());

            // TODO: For now, we're going to require that all image processing shaders share the same configuration
            // structure.
            m_cbParams = m_device->createBuffer(sizeof(ImageProcessorConfig), "Postprocess CB");

            updateConfig();
        }

        bool checkUpdateConfig(PostProcessType mode) const {
            if (mode != PostProcessType::Off) {
                return m_configManager->hasChanged(SettingPostSunGlasses) ||
                       m_configManager->hasChanged(SettingPostContrast) ||
                       m_configManager->hasChanged(SettingPostBrightness) ||
                       m_configManager->hasChanged(SettingPostExposure) ||
                       m_configManager->hasChanged(SettingPostSaturation) ||
                       m_configManager->hasChanged(SettingPostVibrance) ||
                       m_configManager->hasChanged(SettingPostHighlights) ||
                       m_configManager->hasChanged(SettingPostShadows) ||
                       m_configManager->hasChanged(SettingPostFakeHDR) ||
                       m_configManager->hasChanged(SettingPostColorGainR) ||
                       m_configManager->hasChanged(SettingPostColorGainG) ||
                       m_configManager->hasChanged(SettingPostColorGainB) ||
                       m_configManager->hasChanged(SettingPostChromaticCorrectionR) ||
                       m_configManager->hasChanged(SettingPostChromaticCorrectionB) ||
                       m_configManager->hasChanged(SettingPostColorSpace) ||
                       m_configManager->hasChanged(SettingPostDither);
            } else {
                return m_configManager->hasChanged(SettingPostColorGainR) ||
                       m_configManager->hasChanged(SettingPostColorGainB) ||
                       m_configManager->hasChanged(SettingPostColorSpace) ||
                       m_configManager->hasChanged(SettingPostDither);
            }
        }

        void updateConfig() {
            using namespace DirectX;
            using namespace xr::math;
            using namespace utilities;

            // Transform normalized user settings to shader values:
            // - reduce brighness range  (scale: 0.8)
            // - increase exposure range (scale: 3.0)
            // - limit shadows range     (scale: 0.5)
            // - inverse highlight range (scale:-1.0)

            static constexpr XMVECTORF32 kGainBias[3][2] = {
                {{{{+2.0f, 1.6f, 6.0f, 2.0f}}}, {{{+1.0f, 0.8f, 3.0f, 1.0f}}}}, // ((v * 2) - 1)  -> [-1..+1]
                {{{{+2.0f, 2.0f, 2.0f, 1.0f}}}, {{{+1.0f, 1.0f, 1.0f, 0.0f}}}}, // ((v * 2) - 1)  -> [-1..+1] (w: [0..+1])
                {{{{-1.0f, 0.5f, 1.0f, 1.0f}}}, {{{-1.0f, 0.0f, 0.0f, 0.0f}}}}, // ((v * 1) - 0)  -> [ 0..+1]
            };

            const auto sunglasses = m_configManager->getEnumValue<PostSunGlassesType>(SettingPostSunGlasses);
            const auto preset = GetPreset(static_cast<size_t>(to_integral(sunglasses)));
            const auto params = GetParams(m_configManager.get(), 0);

            // [0..1000] -> [0..1] * Gain - Bias
            for (size_t i = 0; i < 3; i++) {
                const auto param = XMVectorSaturate((XMLoadSInt4(&params[i]) + XMLoadSInt4(&preset[i])) * 0.001f);
                StoreXrVector4(&m_config.Params1 + i, (param * kGainBias[i][0]) - kGainBias[i][1]);
            }

            // Color space: reinterpret the linear input as if it was in another gamut, without color management.
            // - vivid: the input is treated as the wider space and converted to Rec.709 (stretches the colors out)
            // - strong: same as vivid, but blended three quarters of the way from the identity (Rec.2020 only)
            // - medium: same as vivid, but blended halfway with the identity (Rec.2020 only)
            // - soft:  the input is treated as Rec.709 and converted to the wider space (mutes the colors)
            // The 4th component of the first row is the on/off flag (the matrix is ignored by the shader when off).
            static constexpr XrVector4f kColorSpace[to_integral(PostColorSpaceType::MaxValue)][3] = {
                {{1.0f, 0.0f, 0.0f, 0.0f}, {0.0f, 1.0f, 0.0f, 0.0f}, {0.0f, 0.0f, 1.0f, 0.0f}}, // Normal
                {{1.6605f, -0.5876f, -0.0728f, 1.0f},
                 {-0.1246f, 1.1329f, -0.0083f, 0.0f},
                 {-0.0182f, -0.1006f, 1.1187f, 0.0f}}, // Rec.2020 -> Rec.709
                {{1.4954f, -0.4407f, -0.0546f, 1.0f},
                 {-0.0935f, 1.0997f, -0.0062f, 0.0f},
                 {-0.0136f, -0.0754f, 1.0890f, 0.0f}}, // Rec.2020 -> Rec.709, three quarters of the way from identity
                {{1.3302f, -0.2938f, -0.0364f, 1.0f},
                 {-0.0623f, 1.0665f, -0.0042f, 0.0f},
                 {-0.0091f, -0.0503f, 1.0594f, 0.0f}}, // Rec.2020 -> Rec.709, halfway to identity
                {{0.6274f, 0.3293f, 0.0433f, 1.0f},
                 {0.0691f, 0.9195f, 0.0114f, 0.0f},
                 {0.0164f, 0.0880f, 0.8956f, 0.0f}}, // Rec.709 -> Rec.2020
                // Disabled for now, may come back later (re-enable together with PostColorSpaceType).
                // {{1.2249f, -0.2249f, 0.0f, 1.0f},
                //  {-0.0421f, 1.0421f, 0.0f, 0.0f},
                //  {-0.0196f, -0.0786f, 1.0983f, 0.0f}}, // Display P3 (D65) -> Rec.709
                // {{0.8225f, 0.1775f, 0.0f, 1.0f},
                //  {0.0332f, 0.9668f, 0.0f, 0.0f},
                //  {0.0171f, 0.0724f, 0.9105f, 0.0f}}, // Rec.709 -> Display P3 (D65)
                // {{1.3984f, -0.3984f, 0.0f, 1.0f},
                //  {0.0f, 1.0f, 0.0f, 0.0f},
                //  {0.0f, -0.0429f, 1.0429f, 0.0f}}, // Adobe RGB (D65) -> Rec.709
                // {{0.7151f, 0.2849f, 0.0f, 1.0f},
                //  {0.0f, 1.0f, 0.0f, 0.0f},
                //  {0.0f, 0.0412f, 0.9588f, 0.0f}}, // Rec.709 -> Adobe RGB (D65)
            };

            const auto colorSpace = static_cast<size_t>(
                to_integral(m_configManager->getEnumValue<PostColorSpaceType>(SettingPostColorSpace)));
            m_config.Params5 = kColorSpace[colorSpace][0];
            m_config.Params6 = kColorSpace[colorSpace][1];
            m_config.Params7 = kColorSpace[colorSpace][2];
            // The 4th component of the other rows is kept free: the dithering is patched JIT in process().

            m_useDither = m_configManager->getValue(SettingPostDither) != 0;

            // CA Correction stuff.
            if (m_mode == PostProcessType::CACorrection) {
                m_config.Params3.w = 1;
                m_config.Params4.x = m_configManager->getValue(SettingPostChromaticCorrectionR) / 100000.0f;
                m_config.Params4.y = 1.0f;
                m_config.Params4.z = m_configManager->getValue(SettingPostChromaticCorrectionB) / 100000.0f;
                // Params4.w is patched JIT in process().
            } else {
                m_config.Params3.w = 0;
            }
        }

        // Size of one code value of the output format (the amplitude of the dithering noise), 0 when none is needed.
        static float GetDitherAmplitude(int64_t format) {
            switch ((DXGI_FORMAT)format) {
            case DXGI_FORMAT_R32G32B32A32_FLOAT:
            case DXGI_FORMAT_R16G16B16A16_FLOAT:
            case DXGI_FORMAT_R11G11B10_FLOAT:
            case DXGI_FORMAT_R32G32B32_FLOAT:
                // Floating point formats do not band.
                return 0.0f;

            case DXGI_FORMAT_R16G16B16A16_UNORM:
                return 1.0f / 65535.0f;

            case DXGI_FORMAT_R10G10B10A2_UNORM:
                return 1.0f / 1023.0f;

            case DXGI_FORMAT_R8G8B8A8_UNORM:
            case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
            case DXGI_FORMAT_B8G8R8A8_UNORM:
            case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
            case DXGI_FORMAT_B8G8R8X8_UNORM:
            case DXGI_FORMAT_B8G8R8X8_UNORM_SRGB:
            default:
                return 1.0f / 255.0f;
            };
        }

        static std::array<DirectX::XMINT4, 3> GetParams(const IConfigManager* configManager, size_t index) {
            using namespace DirectX;
            if (configManager) {
                static const char* lut[] = {"", "_u1", "_u2", "_u3", "_u4"}; // placeholder up to 4
                const auto suffix = lut[std::min(index, std::size(lut))];

                return {XMINT4(configManager->getValue(SettingPostContrast + suffix),
                               configManager->getValue(SettingPostBrightness + suffix),
                               configManager->getValue(SettingPostExposure + suffix),
                               configManager->getValue(SettingPostSaturation + suffix)),

                        XMINT4(configManager->getValue(SettingPostColorGainR + suffix),
                               configManager->getValue(SettingPostColorGainG + suffix),
                               configManager->getValue(SettingPostColorGainB + suffix),
                               configManager->getValue(SettingPostFakeHDR + suffix)),

                        XMINT4(configManager->getValue(SettingPostHighlights + suffix),
                               configManager->getValue(SettingPostShadows + suffix),
                               configManager->getValue(SettingPostVibrance + suffix),
                               0)};
            }
            return {XMINT4(500, 500, 500, 500), XMINT4(500, 500, 500, 0), XMINT4(1000, 0, 0, 0)};
        }

        static std::array<DirectX::XMINT4, 3> GetPreset(size_t index) {
            using namespace DirectX;

            // standard presets
            static constexpr std::array<XMINT4, 3> lut[to_integral(PostSunGlassesType::MaxValue)] = {
                // none
                {{{0, 0, 0, 0}, {0, 0, 0, 0}, {0, 0, 0, 0}}},

                // sunglasses light: +2.5 contrast, -5 bright, -5 expo, -20 high
                {{{25, -50, -50, 0}, {0, 0, 0, 0}, {-20, 0, 0, 0}}},

                // sunglasses dark: +2.5 contrast, -10 bright, -10 expo, -40 high, +5 shad
                {{{25, -100, -100, 0}, {0, 0, 0, 0}, {-400, 50, 0, 0}}},

                //// deep night (beta1): +0.5 contrast, -40 bright, +20 expo, -15 sat, -75 high, +15 shad, +5 vib
                //{{{5, -400, 200, -150}, {0, 0, 0, 0}, {-750, 150, 50, 0}}},

                // deep night (beta2): +5 contrast, -30 bright, +25 expo, +5 sat, -100 high, +10 shad
                {{{50, -300, 250, +50}, {0, 0, 0, 0}, {-1000, 100, 0, 0}}},
            };

            return lut[index < std::size(lut) ? index : 0];
        }

        const std::shared_ptr<IConfigManager> m_configManager;
        const std::shared_ptr<IDevice> m_device;
        const std::array<DirectX::XMINT4, 3> m_userParams;

        std::shared_ptr<IQuadShader> m_shaders[2]; // off, on
        std::shared_ptr<IShaderBuffer> m_cbParams;

        PostProcessType m_mode{PostProcessType::Off};
        bool m_useDither{false};
        ImageProcessorConfig m_config{};
    };

} // namespace

namespace toolkit::graphics {

    bool IsDeviceSupportingFP16(std::shared_ptr<IDevice> device) {
        if (device) {
            if (auto device11 = device->getAs<D3D11>()) {
                D3D11_FEATURE_DATA_SHADER_MIN_PRECISION_SUPPORT feature = {};
                device11->CheckFeatureSupport(D3D11_FEATURE_SHADER_MIN_PRECISION_SUPPORT, &feature, sizeof(feature));
                return (feature.PixelShaderMinPrecision & D3D11_SHADER_MIN_PRECISION_16_BIT) != 0;
            }
            if (auto device12 = device->getAs<D3D12>()) {
                D3D12_FEATURE_DATA_D3D12_OPTIONS feature = {};
                device12->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS, &feature, sizeof(feature));
                return (feature.MinPrecisionSupport & D3D12_SHADER_MIN_PRECISION_SUPPORT_16_BIT) != 0;
            }
        }

        return false;
    }

    GpuArchitecture GetGpuArchitecture(UINT VendorId) {
        // Known PCI vendor IDs
        constexpr uint32_t kVendorID_AMD = 0x1002;
        constexpr uint32_t kVendorID_Intel = 0x8086;
        constexpr uint32_t kVendorID_NVIDIA = 0x10DE;

        return VendorId == kVendorID_AMD      ? GpuArchitecture::AMD
               : VendorId == kVendorID_Intel  ? GpuArchitecture::Intel
               : VendorId == kVendorID_NVIDIA ? GpuArchitecture::NVidia
                                              : GpuArchitecture::Unknown;
    }

    GpuArchitecture GetGpuArchitecture(std::shared_ptr<IDevice> device) {
        if (device) {
            std::string name = device->getDeviceName();
            std::transform(name.begin(), name.end(), name.begin(), [](unsigned char c) { return std::tolower(c); });

            if (name.find("nvidia") != std::string::npos)
                return GpuArchitecture::NVidia;

            if (name.find("intel") != std::string::npos)
                return GpuArchitecture::Intel;

            // put last in case other vendor names have these 3 letters in their device name.
            if (name.find("amd") != std::string::npos)
                return GpuArchitecture::AMD;
        }

        return GpuArchitecture::Unknown;
    }

    std::shared_ptr<IImageProcessor> CreateImageProcessor(std::shared_ptr<IConfigManager> configManager,
                                                          std::shared_ptr<IDevice> graphicsDevice) {
        return std::make_shared<ImageProcessor>(configManager, graphicsDevice);
    }

} // namespace toolkit::graphics
