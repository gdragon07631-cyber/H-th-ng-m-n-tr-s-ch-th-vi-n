# Hiển thị và theo dõi bản sao — Lát 2

Trang chi tiết đầu sách có bảng chỉ đọc gồm mã vạch, kho, kệ, ngày nhập,
giá bìa, tình trạng vật lý và trạng thái. Bảng chỉ lấy bản sao của đầu sách
đang xem, sắp theo mã vạch. Tổng số và số **Sẵn sàng** được tính từ cùng danh
sách này; khi thêm bản sao qua form Lát 1 và trở lại chi tiết, dữ liệu được nạp lại.

Bảng hỗ trợ năm nhãn: **Sẵn sàng**, **Đang mượn**, **Đang giữ cho đặt trước**,
**Đang sửa chữa**, **Đã loại khỏi kho**. Giá trị nghiệp vụ cũ `Đang giữ` được
giữ nguyên và chỉ đổi nhãn ở bảng mới. Không bổ sung thao tác đổi trạng thái.
Bản sao đã loại vẫn được tính vào tổng số, chỉ trạng thái `Sẵn sàng` tính vào
số bản sẵn sàng. Dữ liệu cũ chưa có ngày nhập hoặc giá bìa hiển thị `Chưa có`.

Không cần migration mới cho Lát 2; tiếp tục dùng schema và dữ liệu Lát 1.
Các luồng sửa bản sao hiện có vẫn giữ nguyên đầu sách của bản sao.

Kiểm tra: `dotnet build --no-restore` và
`dotnet test tests/Project.Tests/Project.Tests.csproj --no-restore`.
