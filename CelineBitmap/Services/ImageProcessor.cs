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
        var bgr = new Mat();

        // BGRA 이미지라면 일단 BGR로 변환!
        if (source.Channels() == 4)
        {
            Cv2.CvtColor(source, bgr, ColorConversionCodes.BGRA2BGR);
        }
        else
        {
            source.CopyTo(bgr);
        }

        var hsv = new Mat();

        // BGR -> HSV
        // Hue(색상), Saturation(채도), Value(명도)
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

        // 세 가지 채널 H, S, V 각각 따로 분리하기
        // 예를 들어 channels[0]에는 Hue 정보만 담긴 흑백 이미지
        Mat[] channels = Cv2.Split(hsv);

        // 채도를 조절할 빈 이미지 공간
        var saturation = new Mat();

        // 채도 값에 배수(factor)를 곱해 진하게 ~ 흐리게 만들기
        // ConvertTo 함수는 채널의 데이터 형태를 바꿀때 사용
        channels[1].ConvertTo(saturation, channels[1].Type(), factor);

        // 메모리 누수를 방지하기 위해 기존의 원본 채도 이미지 channel[1]을 메모리에서 해제한다
        channels[1].Dispose();

        // 방금 factor를 곱해 새로 만든 채도 이미지
        channels[1] = saturation;

        // 분리된 채널들을 하나로 합치기
        Cv2.Merge(channels, hsv);

        // 다시 일반 이미지(BGR)로 되돌리기
        var result = new Mat();
        Cv2.CvtColor(hsv, result, ColorConversionCodes.HSV2BGR);

        // OpenCV의 Mat 객체는 메모리를 계속 차지하게 된다
        // 사용이 끝난 channels 배열 안의 이미지들, hsv, 원본 이미지를 모두 안전하게 해제한다
        foreach (Mat channel in channels) channel.Dispose();
        hsv.Dispose();
        bgr.Dispose();

        return result;
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