# UniversalParser

一个支持多数容器型文件格式解析的简单工具。

[English](README.md) | 中文

## 功能

- 支持解析MPEG（ISOBMFF）、JPEG、PNG、RIFF、EBML、FLV、FBX等多种*容器/分块*型文件框架格式。

- 支持解析以上格式中的多数子Box/Chunk，并且输出可读的结果字段。

> [!NOTE]
>
> 此程序仅支持“原始解析”，即：在文件上只支持按照文件的二进制树结构切分chunk到可视化节点，然后将各个二进制数据块中的一切可读字段如实地拿出来解析，并合理转换、输出人类可读的解析结果。并不支持实际数据载荷的解析、校验、解码、转封装等等功能。

### 程序功能

- 遵循原始值呈现。不私自加单位、换算、修改、更正。但如果遇到必须换算或解码的值，则采用同名尖括号包裹的新条目表示。

- 适宜的关键说明。如果遇到非标准块、已明确的错误值、意外出现的数据或截断的残留，则适当地添加`<Note>`以提示。

- 高性能用户界面。ListView虚拟化，以使得超大数组解析结果不卡顿页面；高性能流式HexViewer只读显示该块完整二进制数据和对应ASCII Dump。

- 解析结果可导出。右键左侧分支图表项，可导出完整解析结果TXT文本，以便交给其他程序或者AI进行分析。*注：谨慎使用，因为超大体积块转TXT文本性能不高、体积膨胀。*

## 已经支持的格式

|容器类型|具体格式（后缀）|内容子块|
|-|-|-|
|MPEG （ISOBMFF）|mp4 m4v m4a mov m4s heic avif等|ftyp moov moof stsd stco udta meta等|
|RIFF|avi wav ani wem webp等|LIST（INFO） fmt  data fact cue  avih idx1 VP8X等|
|JPG|jpg jpeg jpe jfjf|FFD8 FFD9 FFE1 FFC1 FFC2 FFC3 FFDA等|
|PNG|png|IDAT IHDR iTxt zTxt sBIT fcTL iCCP bKGD tRNS等|
|EBML|mkv webm|所有常见元素|
|FBX（二进制格式）|fbx|所有|
|FLV|flv|所有|
|ASF|asf wmv|（正在开发中，支持性暂不佳）|

同时还支持上述文件中可能内嵌的`ID3v2 Tag`和`Exif`二进制数据完整解析。

兜底设计：如遇错误文件/暂不支持的文件格式，默认将整个文件当作二进制RawData单块读取。

## 计划支持的格式

- [ ] ASF
- [ ] Flash SWF
- [ ] IFF

## 程序GUI示例截图

![Main](Images/1.png)
![Main](Images/2.png)
![Main](Images/3.png)

## 脚注和声明

- 此程序大量使用AI生成代码，包括但不限于DeepSeek v4 flash、Claude opus 5、ChatGPT*、Gemini网页版（排序先后表示代码贡献量大小）。但为确保质量和正确性，均要求AI逐个搜索并按照官方规范生成代码模块，得到代码后手动调整和修改，并在本地进行多次实际文件上的测试，通过后才采用。

- 由于AI编程的上下文限制和人工要求的取舍，显式地采取了较弱的解析安全性措施。故该程序可能在某些已解析的块上出现意外错误，并且如果该错误由恶意构建的错误文件导致，则有一定概率导致程序崩溃，也有一定概率导致老旧CVE漏洞复发。虽然此程序严格不解析任何实际数据载荷，但不完全确保在文件容器格式上能避免漏洞复发。在使用该程序解析任何文件时，请先确保文件本身来源可靠、没有恶意错误结构。
