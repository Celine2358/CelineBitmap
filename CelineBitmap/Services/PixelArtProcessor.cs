using System;
using OpenCvSharp;

namespace CelineBitmap.Services;

public static class PixelArtProcessor
{
    /// <summary>
    /// 일러스트 이미지를 픽셀아트 스타일로 변환한다.
    ///
    /// pixelWidth: 내부적으로 축소할 픽셀 해상도의 가로 크기
    /// paletteColors: K-Means가 생성할 대표 색상의 수
    /// </summary>
    public static Mat Create(Mat source, int pixelWidth, int paletteColors, bool smooth = true)
    {
        if (source.Empty())
        {
            throw new ArgumentException("원본 이미지가 비어 있습니다.", nameof(source));
        }

        // 가로 도트 개수를 최소 8개에서 최대 원본 이미지 가로 크기 사이로 제한
        // 8보다 작으면 형체를 알아볼 수 없었고
        // 원본보다 크면 픽셀아트가 아님
        pixelWidth = Math.Clamp(pixelWidth, 8, source.Width);

        // 픽셀아트에 사용할 색상 수를 최소 2가지에서 최대 64가지 사이로 제한
        paletteColors = Math.Clamp(paletteColors, 2, 64);

        // 원본 비율을 유지한 Pixel Art 높이 계산
        double ratio = (double)source.Height / source.Width;
        int pixelHeight = Math.Max(1, (int)Math.Round(pixelWidth * ratio));

        // 픽셀아트화에서 알파가 사라지는 것 수정
        // BGR / Alpha 분리하기
        Mat? alpha = null;
        Mat bgr = new();

        if (source.Channels() == 4)
        {
            // 이미지가 4채널(BGRA)인 경우, 각 채널을 분리한다
            Mat[] channels = Cv2.Split(source);

            // 색상 채널만 따로 합쳐서 bgr 변수에 저장한다
            Cv2.Merge(new[] { channels[0], channels[1], channels[2] }, bgr);
            // 4번째인 알파(투명) 채널을 alpha 변수에 복사해두기
            alpha = channels[3].Clone();

            // 분리에 사용된 임시 채널 메모리 해제
            foreach (Mat channel in channels) channel.Dispose();
        }
        else
        {
            // 이미지에 알파가 없다면 BGR로 변환하여 사용
            bgr = ConvertToBgr(source);
        }

        using (bgr)
        {
            using var preprocessed = new Mat();

            // smooth 옵션이 켜져 있으면 BilateralFilter를 적용
            if (smooth) Cv2.BilateralFilter(bgr, preprocessed, 5, 40, 40);
            // 그렇지 않으면 원본 BGR 이미지를 그대로 전처리 이미지로 복사
            else bgr.CopyTo(preprocessed);

            using var small = new Mat();
            // 이미지를 원하는 픽셀 아트 크기로 강제로 줄인다 (Area 보간)
            Cv2.Resize(preprocessed, small, new Size(pixelWidth, pixelHeight), 0, 0, InterpolationFlags.Area);

            // K-Means 알고리즘을 사용하여 지정된 팔레트 색상 수 만큼 색상 제한
            using Mat quantized = QuantizeKMeans(small, paletteColors);
            var resultBgr = new Mat();

            Cv2.Resize(quantized, resultBgr, source.Size(), 0, 0, InterpolationFlags.Nearest);

            // Alpha가 없으면 기존처럼 BGR 반환
            if (alpha is null) return resultBgr;

            using (alpha)
            using (resultBgr)
            {
                // Alpha도 같은 픽셀 Grid로 변환
                using var smallAlpha = new Mat();
                Cv2.Resize(alpha, smallAlpha, new Size(pixelWidth, pixelHeight), 0, 0, InterpolationFlags.Area);

                // 축소된 알파 채널을 다시 원본 크기로 확대 (도트 형태 유지)
                using var largeAlpha = new Mat();
                Cv2.Resize(smallAlpha, largeAlpha, source.Size(), 0, 0, InterpolationFlags.Nearest);

                // BGR + Alpha 재결합
                Mat[] channels = Cv2.Split(resultBgr);

                try
                {
                    var result = new Mat();
                    // 픽셀화된 BGR 채널들과, 똑같이 픽셀화한 알파(largeAlpha) 채널을
                    // 하나로 합쳐 4채널(BGRA) 이미지를 만든다
                    Cv2.Merge(new[] {channels[0], channels[1], channels[2], largeAlpha }, result);

                    return result;
                }
                finally
                {
                    // 메모리 누수 방지
                    foreach (Mat channel in channels) channel.Dispose();
                }
            }
        }
    }


    /// <summary>
    /// 이미지를 OpenCV의 일반적인 3채널 BGR 이미지로 맞춘다.
    /// </summary>
    static Mat ConvertToBgr(Mat source)
    {
        var result = new Mat();

        switch (source.Channels())
        {
            case 4:
                Cv2.CvtColor(source, result, ColorConversionCodes.BGRA2BGR);
                break;

            case 3:
                source.CopyTo(result);
                break;

            case 1:
                Cv2.CvtColor(source, result, ColorConversionCodes.GRAY2BGR);
                break;

            default:
                result.Dispose();

                throw new NotSupportedException($"지원하지 않는 채널 수: {source.Channels()}");
        }

        return result;
    }


    /// <summary>
    /// BGR 픽셀들을 K개의 색상 군집으로 나누고,
    /// 각각을 가장 가까운 대표 색으로 변경한다
    /// </summary>
    static Mat QuantizeKMeans(Mat source, int colors)
    {
        if (source.Type() != MatType.CV_8UC3)
        {
            throw new ArgumentException("K-Means 입력 이미지는 CV_8UC3이어야 합니다.", nameof(source));
        }

        /* 원래 이미지: Height × Width × 3
         * K-Means 입력: PixelCount × 3
         * 즉 한 Pixel을 [B, G, R]이라는 3차원 점 하나로 본다
         */
        using Mat reshaped = source.Reshape(1, source.Rows * source.Cols);

        // K-Means는 CV_32F 데이터를 사용한다.
        using var samples = new Mat();
        reshaped.ConvertTo(samples, MatType.CV_32F);

        using var labels = new Mat();
        using var centers = new Mat();

        /* labels: 각 Pixel이 어떤 군집에 속했는지
         * centers: 각 군집의 대표 BGR 색상
         */
        Cv2.Kmeans
            (samples, colors, labels, new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 20, 1.0), 3, KMeansFlags.PpCenters, centers);

        var result = new Mat(source.Rows, source.Cols, MatType.CV_8UC3);

        /* OpenCvSharp5의 AsRows<T>() 사용!
         * 매 Pixel마다 Native 호출을 반복하지 않고
         * 행 단위 메모리 접근을 한다
         */

        // 새로 칠할 result의 픽셀 데이터 접근자(Vec3b는 B, G, R 3바이트 컬러 구조체)
        var outputRows = result.AsRows<Vec3b>();
        
        // 각 픽셀이 어떤 대표 색상에 속하는지 적혀 있는 번호표 labels 목록
        var labelRows = labels.AsRows<int>();

        // K-Means가 찾아낸 대표 색상들의 실제 B, G, R값 centers 목록
        var centerRows = centers.AsRows<float>();

        // 이미지 전체를 도는 2중 반복문 (Y축 -> X축)
        int index = 0;

        for (int y = 0; y < result?.Rows; y++)
        {
            Span<Vec3b> row = outputRows[y];

            for (int x = 0; x < result?.Cols; x++)
            {
                // 픽셀에 채워 넣을 대표 색상 찾아와!
                int cluster = labelRows[index][0];

                // 방금 알아낸 cluster를 이용해
                // 그 색상의 진짜 BGR float값이 들어있는 메모리를 가져와!
                Span<float> center = centerRows[cluster];

                // center 배열에 저장된 BGR float 값을 정수형으로 바꿈
                // 0~255 고정
                byte b = (byte)Math.Clamp(Math.Round(center[0]), 0, 255);
                byte g = (byte)Math.Clamp(Math.Round(center[1]), 0, 255);
                byte r = (byte)Math.Clamp(Math.Round(center[2]), 0, 255);

                // 현재 행의 X번째 픽셀 공간에 방금 계산한 안전한 Vec3b를 넣는다
                row[x] = new Vec3b(b, g, r);

                // 다음 픽셀 처리
                index++;
            }
        }

        return result;
    }
}