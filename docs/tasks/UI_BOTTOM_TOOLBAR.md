Đọc AGENTS.md trước khi thực hiện.



Hãy sửa UI trong `TestWindow.xaml` để phần BOTTOM TOOLBAR hiện/ẩn theo kiểu layout resize, KHÔNG overlay lên DataGrid.



Mục tiêu UI:



1\. Bottom toolbar hiện tại đang nổi đè lên phần DataGrid.

2\. Tôi muốn thay đổi thành:



&#x20;  \* Khi toolbar ẩn: DataGrid chiếm toàn bộ phần chiều cao còn lại.

&#x20;  \* Khi rê chuột xuống vùng đáy cửa sổ: Bottom toolbar xuất hiện.

&#x20;  \* Khi toolbar xuất hiện: layout phải dành chiều cao thật cho toolbar, làm DataGrid co lại và bị đẩy lên trên.

&#x20;  \* Toolbar tuyệt đối không che/overlay bất kỳ row nào của DataGrid.

&#x20;  \* Khi chuột rời vùng kích hoạt/toolbar: toolbar ẩn lại và DataGrid giãn xuống.



Hãy ưu tiên sửa bằng WPF layout chuẩn.



Kiến trúc mong muốn trong `TestWindow.xaml`:



\* Dùng `Grid` với RowDefinitions.

\* Row chứa nội dung/DataGrid dùng `Height="\*"`.

\* Row chứa Bottom Toolbar dùng chiều cao thay đổi theo trạng thái.

\* Không đặt Bottom Toolbar bằng Canvas hoặc cách positioning overlay lên DataGrid.

\* Không dùng negative Margin hoặc hack TranslateTransform để giả lập việc đẩy DataGrid.

\* Không đặt toolbar chồng cùng Grid.Row với DataGrid nếu điều đó gây overlay.



Ví dụ concept:



<Grid>

&#x20;   <Grid.RowDefinitions>

&#x20;       ...

&#x20;       <RowDefinition Height="\*" />

&#x20;       <RowDefinition Height="Auto" />

&#x20;   </Grid.RowDefinitions>



```

<!-- Main/DataGrid area -->

<Grid Grid.Row="...">

&#x20;   ...

</Grid>



<!-- Bottom toolbar -->

<Border Grid.Row="..."

&#x20;       Visibility="...">

&#x20;   ...

</Border>

```



</Grid>



Khi toolbar Visible/Collapsed:



\* Visible phải làm row toolbar có chiều cao thật.

\* Collapsed phải giải phóng chiều cao để DataGrid tự giãn xuống.



Nếu code hiện tại đang có cơ chế hover detector ở đáy:



\* giữ lại behavior hiện tại nếu hợp lý;

\* chỉ thay đổi layout để toolbar không overlay;

\* detector có thể là vùng rất mỏng ở đáy khi toolbar đang ẩn.



Nếu cần một vùng hover kích hoạt:



\* vùng kích hoạt phải không che nội dung đáng kể;

\* có thể dùng một row rất nhỏ hoặc cơ chế MouseEnter phù hợp;

\* khi toolbar mở, toolbar phải nằm trong row riêng.



Yêu cầu behavior:



STATE 1 — toolbar ẩn:



+--------------------------------------+

|                                      |

|            DataGrid                  |

|                                      |

|                                      |

+--------------------------------------+

| vùng hover rất nhỏ nếu cần           |

+--------------------------------------+



STATE 2 — rê chuột xuống đáy:



+--------------------------------------+

|                                      |

|            DataGrid                  |

|                                      |

+--------------------------------------+

|         BOTTOM TOOLBAR               |

+--------------------------------------+



Không được thành:



+--------------------------------------+



| DataGrid                                 |            |

| ---------------------------------------- | ---------- |

| TOOLBAR                                  | <- overlay |

| +--------------------------------------+ |            |



Hãy kiểm tra thêm:



\* ScrollViewer/DataGrid vẫn scroll bình thường.

\* Row cuối của DataGrid không bị che.

\* Nút `DỪNG AN TOÀN` và `VỀ TRANG CHÍNH` vẫn hoạt động như cũ.

\* Không thay đổi command binding/ViewModel nếu không cần.

\* Không sửa business logic.

\* Không thay đổi các phần UI khác ngoài phạm vi cần thiết.

\* Giữ style/màu/font hiện tại.

\* Không làm mất trạng thái hover hiện có nếu có thể tái sử dụng.



Nếu cơ chế hiện tại dùng:



\* Popup;

\* Adorner;

\* Canvas;

\* absolute alignment;

\* Grid overlay;

\* ZIndex;



hãy xác định chính xác nguyên nhân toolbar đang đè lên DataGrid và chuyển nó sang layout row riêng.



Nếu cần sửa code-behind để điều khiển Visibility/height:



\* sửa tối thiểu;

\* ưu tiên trigger/binding WPF hiện có trước;

\* không tạo timer/polling mới nếu không cần.



Sau khi sửa:



1\. Build project.

2\. Kiểm tra XAML compile.

3\. Kiểm tra WPF binding errors liên quan.

4\. Review diff chỉ cho thay đổi liên quan Bottom Toolbar.

5\. Không refactor phần khác.

6\. Không thay đổi D2XX/UART/test logic.



Nếu build/test PASS và Git workflow trong AGENTS.md cho phép:



\* commit thay đổi;

\* push theo workflow đã thiết lập.



Commit message gợi ý:



`fix: make test bottom toolbar resize content instead of overlay`



Cuối cùng báo:



\* nguyên nhân layout cũ bị overlay;

\* file đã sửa;

\* cấu trúc Grid.Row trước/sau;

\* cách toolbar được show/hide;

\* xác nhận DataGrid hiện bị co lên thay vì bị toolbar che;

\* build result;

\* commit/push status.



