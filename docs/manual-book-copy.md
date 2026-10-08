# Thêm bản sao cá biệt — Lát 1

Từ chi tiết đầu sách, nhân viên thư viện chọn **Thêm bản sao**, nhập mã vạch thủ công,
kho, kệ, ngày nhập, giá bìa và tình trạng vật lý. Mã vạch duy nhất toàn hệ thống;
nếu trùng, form hiển thị ID bản sao và đầu sách đang sử dụng mã đó. Bản sao mới có
trạng thái **Sẵn sàng** và đầu sách được giữ cố định khi sửa bản sao.

Áp dụng migration `20261005100000_AddBookCopyCoverPrice` trước khi sử dụng form
(`dotnet run -- --migrate-database`, với kết nối database đã cấu hình). Cột giá bìa
cho phép null để giữ nguyên bản sao cũ.

Giá bìa không âm và tối đa hai chữ số thập phân. Kho và kệ phải đang hoạt động,
kệ phải thuộc kho đã chọn. Form giữ lại dữ liệu nhập nếu có lỗi.

Kiểm tra: `dotnet build` và `dotnet test tests/Project.Tests/Project.Tests.csproj`.
