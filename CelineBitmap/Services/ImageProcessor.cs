using System;
using System.IO;
using Avalonia.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.AvaloniaExtensions;

namespace CelineBitmap.Services;

// Mat: 이미지나 행렬의 픽셀 데이터를 저장/관리하는 OpenCV 핵심 자료구조
// Cv2: OpenCV의 이미지 처리 함수들을 제공하는 static 클래스
// Size: Width와 Height를 표현하는 구조체
public static class ImageProcessor
{
    /// <summary>
    /// PNG, JPG 등의 이미지를 OpenCV Mat으로 불러오기
    /// </summary>
    public static Mat Load(string path)
    {
        // Unchanged: PNG의 Alpha 채널 등이 존재하면 가능한 그대로 유지
        Mat image = Cv2.ImRead(path, ImreadModes.Unchanged);

        // 파일을 읽지 못했다면 빈 Mat이 반환될 수 있다!!
        if (image.Empty())
        {
            image.Dispose();
            throw new IOException($"이미지를 불러올 수 없습니다: {path}");
        }

        return image;
    }

    /// <summary>
    /// 지정한 보간법으로 이미지의 크기를 변경한다
    /// </summary>
    public static Mat Resize(Mat source, int width, int height, InterpolationFlags interpolation)
    {
        if (source.Empty())
        {
            // 이미지 행렬 데이터 인자가 잘못되었을 때
            throw new ArgumentException("원본 이미지가 비어 있습니다.", nameof(source));
        }

        if (width <= 0 || height <= 0)
        {
            // width와 height 인자의 범위가 잘못되었을 때
            throw new ArgumentOutOfRangeException(nameof(width), "Width와 Height는 1 이상이어야 합니다.");
        }

        // 결과 이미지를 담은 새로운 Mat
        var result = new Mat();

        // Pixel Art: 가장 가까운 픽셀을 그대로 복제한다
        // -> 픽셀 경계를 흐리지 않는다
        // 
        // 일반 이미지 확대: Lanczos4를 사용해 주변 픽셀을 계산한다
        // 일반 이미지 축소: Area가 축소에 비교적 적합할 듯

        Cv2.Resize(source, result, new Size(width, height), 0, 0, interpolation);
        return result;
    }

    /// <summary>
    /// 이미지의 밝기와 대비를 변경한다
    /// brightness: -100 ~ 100
    /// contrast: 0.0 ~ 2.0
    /// </summary>
    public static Mat AdjustBrightnessContrast(Mat source, double brightness, double contrast)
    {
        var result = new Mat();

        /* result = source × contrast + brightness
         *
         * contrast = 1.0 -> 원본 대비
         * contrast > 1.0 -> 명암 차이 증가
         * contrast < 1.0 -> 명암 차이 감소
         *
         * brightness > 0 -> 밝게
         * brightness < 0 -> 어둡게
         */

        source.ConvertTo(result, source.Type(), contrast, brightness);

        return result;
    }

    /// <summary>
    /// Saturation(채도)을 변경한다
    /// factor = 1.0 -> 원본
    /// factor = 0.0 -> 무채색
    /// factor = 2.0 -> 채도 증가
    /// </summary>
    public static Mat AdjustSaturation(Mat source, double factor)
    {
        // Grayscale은 채도라는 개념이 없으므로 그대로 복사
        if (source.Channels() == 1) return source.Clone();

        // BGR과 Alpha 값 분리
        var (bgr, alpha) = SplitBgrAndAlpha(source);

        try
        {
            using var hsv = new Mat();

            // BGR -> HSV
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
            Mat[] channels = Cv2.Split(hsv);

            try
            {
                // channels[0] = 색상(Hue)
                // channels[1] = 채도(Saturation)
                // channels[2] = 명도(Value)
                using var newSaturation = new Mat();

                // S' = S × factor
                channels[1].ConvertTo(newSaturation, channels[1].Type(), factor);
                newSaturation.CopyTo(channels[1]);

                // H + 수정된 S + V
                Cv2.Merge(channels, hsv);
            }
            finally
            {
                foreach (Mat channel in channels) channel.Dispose();
            }

            using var adjustedBgr = new Mat();

            // HSV -> BGR
            Cv2.CvtColor(hsv, adjustedBgr, ColorConversionCodes.HSV2BGR);

            // 원래 가지고 있던 Alpha를 다시 붙인다.
            return MergeBgrAndAlpha(adjustedBgr, alpha);
        }
        finally
        {
            bgr.Dispose();
            alpha?.Dispose();
        }
    }

    /// <summary>
    /// 컬러 이미지를 흑백 이미지로 변환한다
    /// </summary>
    public static Mat Grayscale(Mat source)
    {
        var result = new Mat();

        // Grayscale인 1채널 Mat이 들어오면 OpenCV 예외가 생길 수 있기에 switch문으로 방지
        switch (source.Channels())
        {
            case 4: Cv2.CvtColor(source, result, ColorConversionCodes.BGRA2GRAY);
                break;

            case 3: Cv2.CvtColor(source, result, ColorConversionCodes.BGR2GRAY);
                break;

            case 1: source.CopyTo(result);
                break;

            default: result.Dispose();
                throw new NotSupportedException($"지원하지 않는 채널 수입니다: {source.Channels()}");
        }

        return result;
    }

    public static Mat GaussianBlur(Mat source)
    {
        var result = new Mat();
        // 이미지 전체를 살짝 흐릿하게 만드는 함수
        // new Size는 흐리게 만들 영역의 가로 세로 픽셀 크기, 커질수록 더 흐려진다
        Cv2.GaussianBlur(source, result, new Size(5, 5), 0);

        return result;
    }

    /// <summary>
    /// 원본과 Blur 이미지를 이용하여
    /// 이미지의 경계를 선명하게 만든다
    /// </summary>
    public static Mat Sharpen(Mat source)
    {
        // 흐릿해진 결과물을 담을 blurred 공간을 만들고
        // new Size를 0으로 두고 표준 편차를 3으로 주면
        // 가장 부드럽게 번지는 최적의 블러 크기를 계산해준다
        var blurred = new Mat();
        Cv2.GaussianBlur(source, blurred, new Size(0, 0), 3);

        var result = new Mat();

        /* Original × 1.5 - Blur × 0.5
         *
         * 흐릿한 성분을 빼면서
         * 경계가 더욱 강조된다
         */

        Cv2.AddWeighted(source, 1.5, blurred, -0.5, 0, result);
        blurred.Dispose();

        return result;
    }

    /// <summary>
    /// 이미지에서 강한 윤곽선을 검출한다.
    /// </summary>
    public static Mat Edge(Mat source)
    {
        var gray = new Mat();

        if (source.Channels() == 4)
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
        }
        else if (source.Channels() == 3)
        {
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            source.CopyTo(gray);
        }

        var result = new Mat();

        // 캐니 엣지 검출
        Cv2.Canny(gray, result, 100, 200);
        gray.Dispose();

        return result;
    }

    /// <summary>
    /// 평평한 부분은 부드럽게 하지만 경계는 최대한 살리자
    /// </summary>
    public static Mat Bilateral(Mat source)
    {
        var result = new Mat();
        Cv2.BilateralFilter(source, result, 9, 75, 75);

        return result;
    }

    /// <summary>
    /// 이미지를 완전한 검은색(0)과 흰색(255) 두 가지로만 나누는 이진화 함수
    /// </summary>
    public static Mat Threshold(Mat source, double threshold = 127)
    {
        // 이미지를 Grayscale 함수로 흑백화한다
        using Mat gray = Grayscale(source);
        var result = new Mat();

        // Cv2.Threshold는 이미지의 모든 픽셀을 돌며 기준값보다 밝은지 어두운지 판별하는 함수
        // 기준값은 127이다
        Cv2.Threshold(gray, result, threshold, 255, ThresholdTypes.Binary);

        return result;
    }

    // OpenCV Mat을 Avalonia UI Image에서 사용할 수 있는 WriteableBitmap으로 변환
    public static WriteableBitmap ToBitmap(Mat image)
    {
        return image.ToWriteableBitmap();
    }

    /// <summary>
    /// 입력 이미지를 BGR 색상 데이터와 Alpha 채널로 분리한다.
    /// Alpha가 없는 이미지라면 alpha는 null.
    /// </summary>
    static (Mat bgr, Mat? alpha) SplitBgrAndAlpha(Mat source)
    {
        switch (source.Channels())
        {
            case 4:
                {
                    Mat[] channels = Cv2.Split(source);

                    try
                    {
                        var bgr = new Mat();

                        // B, G, R만 다시 하나의 3채널 이미지로 합친다.
                        Cv2.Merge(new[] { channels[0], channels[1], channels[2] }, bgr);

                        // Alpha는 색 보정에서 건드리지 않도록 따로 복사한다.
                        Mat alpha = channels[3].Clone();

                        return (bgr, alpha);
                    }
                    finally
                    {
                        foreach (Mat channel in channels) channel.Dispose();
                    }
                }

            case 3:
                return (source.Clone(), null);

            case 1:
                {
                    var bgr = new Mat();
                    Cv2.CvtColor(source, bgr, ColorConversionCodes.GRAY2BGR);

                    return (bgr, null);
                }

            default:
                throw new NotSupportedException($"지원하지 않는 채널 수입니다: {source.Channels()}");
        }
    }

    /// <summary>
    /// 처리된 BGR 이미지에 기존 Alpha 채널을 다시 결합한다.
    /// </summary>
    static Mat MergeBgrAndAlpha(Mat bgr, Mat? alpha)
    {
        // 원래 Alpha가 없었다면 BGR 그대로 반환
        if (alpha is null) return bgr.Clone();
        Mat[] channels = Cv2.Split(bgr);

        try
        {
            var result = new Mat();
            Cv2.Merge(new[] { channels[0], channels[1], channels[2], alpha }, result);
            return result;
        }
        finally
        {
            foreach (Mat channel in channels) channel.Dispose();
        }
    }

    /// <summary>
    /// 처리된 Mat을 PNG/JPG 등의 파일로 저장한다.
    /// </summary>
    public static void Save(Mat image, string path)
    {
        if (image.Empty())
        {
            throw new ArgumentException("저장할 이미지가 비어 있습니다.", nameof(image));
        }

        bool success = Cv2.ImWrite(path, image);

        if (!success)
        {
            throw new IOException($"이미지를 저장하지 못했습니다: {path}");
        }
    }
}