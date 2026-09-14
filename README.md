# Celine Bitmap
<img width="320" height="320" alt="CelineBitmap-Icon" src="https://github.com/user-attachments/assets/7070fc36-244c-46e5-83e0-12ba08fb59c5" />

C#과 Avalonia UI, OpenCvSharp5를 이용해 제작하는  
2D 이미지 처리 및 픽셀 아트 변환 연습 프로젝트

---

## 목표
- OpenCV의 기본 이미지 처리 구조
- `Mat` 기반 이미지 데이터 처리
- 이미지 확대 / 축소와 보간법의 차이
- 색상 공간 변환
- 블러, 샤픈, 엣지 검출 등의 필터 처리
- 이미지 픽셀 및 색상 데이터 분석
- Color Quantization과 Dithering
- Pixel Art 변환 알고리즘
- Avalonia UI와 OpenCV 이미지 데이터 연결
- MVVM 기반 데스크톱 애플리케이션 구조
- Git / GitHub Desktop을 이용한 프로젝트 버전 관리

---

## 기술
- **C#**
- **.NET 10**
- **Avalonia UI 12**
- **CommunityToolkit.Mvvm**
- **OpenCvSharp5**
- **OpenCvSharp5.AvaloniaExtensions**
- **Git / GitHub Desktop**

---

### Image I/O
```csharp
Cv2.ImRead();
Cv2.ImWrite();
