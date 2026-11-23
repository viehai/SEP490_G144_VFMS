# Việt hóa và phân trang feature 3–5

> Thực hiện trực tiếp trong checkout hiện tại theo yêu cầu người dùng.

**Goal:** Thông báo, email và lỗi người dùng của feature 3–5 bằng tiếng Việt; danh sách tối đa 10 mục/trang.

**Architecture:** Giữ nguyên mã lỗi, giá trị enum và hợp đồng API. Đổi kích thước trang tại nguồn của feature 3/4; tái sử dụng Pager với PageSize=10, bổ sung phân trang phía client cho danh sách feature 5 đang trả mảng. Lịch tuần và trường lựa chọn giữ đầy đủ ca/lựa chọn.

**Tech Stack:** .NET 8, Blazor, FluentValidation, xUnit.

**Hiện tại:** Feature 3: mặc định 20 (HLV, hồ sơ, đánh giá, lịch sử nộp). Feature 4: 20 (hợp đồng, hội viên, bảo lưu, tranh chấp, gói). Feature 5: danh sách và sơ đồ tủ chưa phân trang.

- [x] Kiểm thử hồi quy: giới hạn trang, sơ đồ tủ, lỗi phiên đăng nhập và xử lý thời gian chờ nộp hồ sơ.
- [x] Việt hóa service, validator, email và nội dung hiển thị; giữ tương thích xử lý email đang chờ và tiền tố lỗi cũ.
- [x] Đặt trang feature 3/4 về 10; thêm trang feature 5, reset khi lọc và giới hạn trang khi dữ liệu giảm.
- [x] Build, chạy bộ test, rà diff và cập nhật ghi nhận thông báo trong NeedRead/NewMSG.txt.

**Ruling:** Yêu cầu đã chỉ rõ ngôn ngữ và số mục, triển khai theo cấu trúc hiện có; không cần bổ sung thiết kế mới hoặc thay schema. Nội dung lưu trước đây trong DB không được tự ý sửa.

Kết quả kiểm chứng và các lỗi có sẵn được ghi ở `../verification/2026-11-23-feature345.md`.
