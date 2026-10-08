# Sinh mã vạch tự động — Lát 3

Form **Thêm bản sao** có hai lựa chọn: **Nhập thủ công** (mặc định) và
**Sinh tự động**. Chế độ tự động ẩn và vô hiệu hóa ô mã vạch; chuyển lại
thủ công giữ nội dung đã nhập. Máy chủ chỉ yêu cầu mã khi nhập thủ công.

Mã tự động có dạng `LIB000001`: lấy phần số lớn nhất của các mã LIB gồm
đúng 6 chữ số trong toàn hệ thống rồi cộng 1, bao gồm mã nhập thủ công.
Mã thuộc dãy khác không ảnh hưởng đến dãy LIB. Khi đã có `LIB999999`,
hệ thống báo hết dãy và không tạo bản sao; nhập thủ công vẫn hoạt động.

Unique index hiện có tiếp tục bảo vệ mã vạch toàn hệ thống. Nếu một yêu cầu
khác lưu cùng mã trong lúc cấp mã, chế độ tự động đọc lại số lớn nhất và
thử cấp lại (tối đa 20 lần); lỗi trùng ở chế độ thủ công vẫn chỉ rõ bản sao
và đầu sách đang giữ mã. Không cấp mã xem trước hay đặt chỗ mã.

Sau khi lưu, thông báo ghi rõ mã vừa sinh và chuyển về chi tiết đầu sách,
nơi bảng cùng tổng số và số Sẵn sàng được nạp lại. Đầu sách luôn lấy từ
route, trạng thái mặc định Sẵn sàng. Không thay đổi luồng tạo lô cũ.

Không cần migration mới cho Lát 3. Cần schema Lát 1 đã được áp dụng.

Kiểm tra:

```powershell
dotnet build --no-restore
dotnet test tests/Project.Tests/Project.Tests.csproj --no-restore
node --test tests/book-copy-barcode-mode.test.cjs
```
