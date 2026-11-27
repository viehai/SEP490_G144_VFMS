# Kết quả rút gọn schema feature 3–6

Số cột lấy từ model EF Core, bao gồm khóa, audit và shadow properties. Không thêm bảng mới. Database ứng dụng chưa được áp dụng migration này.

| Bảng | Trước | Sau | Giảm |
|---|---:|---:|---:|
| TrainerApplication | 29 | 28 | 1 |
| TrainerProfile | 22 | 17 | 5 |
| TrainerRole | 7 | 7 | 0 |
| TrainerReview | 16 | 15 | 1 |
| PTPurchase | 26 | 16 | 10 |
| PTSession | 38 | 31 | 7 |
| PTFreeze | 29 | 22 | 7 |
| PTAvailability | 11 | 8 | 3 |
| Locker | 11 | 7 | 4 |
| LockerZone | 10 | 10 | 0 |
| LockerPricingPolicy | 12 | 10 | 2 |
| LockerRentalContract | 35 | 25 | 10 |
| LockerIncidentLog | 15 | 15 | 0 |
| GymAnnouncement | 11 | 11 | 0 |
| Notification | 13 | 13 | 0 |
| **15 bảng feature 3–6** | **285** | **235** | **50** |
| PaymentTransaction (dùng chung) | 31 | 29 | 2 |
| PackageComponent (dùng chung) | 19 | 17 | 2 |
| **Tổng 17 bảng** | **335** | **281** | **54** |

## Nguồn dữ liệu dùng chung

- Trainer dùng `ApplicationUser.AvatarUrl` và `PhysicalProfile.Gender`. Giữ metadata của ảnh upload và quyền truy cập file hiện có. Hai ngày trạng thái tiếp nhận học viên gộp thành `ReadinessSince`; DTO vẫn trả tên trường cũ.
- PT dùng tên và điều khoản đã mua trên `OrderItem`, giá trên giao dịch thanh toán hoặc giá đơn trừ voucher trước khi tạo giao dịch. Các giá trị không lấy từ bảng giá catalog hiện tại. Thành viên của buổi tập và người yêu cầu bảo lưu lấy từ hợp đồng. Enum gộp trạng thái yêu cầu hủy và thay đổi bảo lưu; API giữ các trạng thái mà client hiện tại hiểu.
- Locker dùng phiên bản chính sách mà hợp đồng đang liên kết. Cập nhật bảng giá tạo phiên bản mới, không sửa giá của hợp đồng cũ. Giữ thời điểm chiếm tủ, sự cố và sức chứa khu tủ.
- `PaymentTransaction` bỏ bản sao `UserId` (lấy `Order.PurchaserUserId`) và phí thay chìa không có luồng sử dụng. Giữ đủ các trường checkout, báo giá, gia hạn, hạn chuyển tiền, giao dịch nguồn và hoàn tiền trên từng khoản thanh toán.
- `PackageComponent` dùng hai giới hạn bảo lưu có sẵn cho PT thay hai cột PT riêng. Ánh xạ các trường API cũ sang nguồn chung; giới hạn GYM/YOGA được giữ nguyên.
- Giữ nguyên Notification và GymAnnouncement. Giữ nguyên số cột của ApplicationUser, PhysicalProfile, UploadedImage, Order, OrderItem và Invoice.

## Dữ liệu lịch sử và migration

Migration `20261127112843_SimplifyFeature3456Schema` chạy trong transaction và khóa các nguồn trong suốt kiểm tra/chuyển dữ liệu. Nó chuyển thông tin hợp lệ sang nguồn chung trước khi xóa cột. Link chứng chỉ và hồ sơ cũ được giữ trong metadata file sẵn có, với quyền hiển thị tương ứng.

Migration dừng và rollback nếu hai nguồn không thống nhất: avatar/gender, quan hệ trainer/member/payment, điều khoản PT đã mua, ngày bảo lưu, thời lượng đề xuất, hoặc giá hợp đồng locker và phiên bản chính sách liên kết. PT thiếu đơn hàng chỉ được nối lại qua một liên kết có thật; không tạo đơn hàng hay thanh toán giả. Đề xuất double-session cũ phải được xử lý trước khi nâng cấp.

Không downgrade các bảng feature đã có dữ liệu bằng cách điền giá trị mặc định vào cột cũ. Muốn phục hồi các trường lịch sử đã loại bỏ phải khôi phục bản sao lưu trước migration.

## Xác minh

- Baseline trước thay đổi: 781 unit test qua; client 631 qua và 7 lỗi dịch thông báo đã tồn tại; trainer workflow/model/pagination 29 qua.
- Model sau thay đổi: 24 kiểm thử hình dạng bảng qua; đã xác nhận có RED trước khi sửa model.
- Sau thay đổi: toàn bộ 787 unit test qua; client vẫn 631 qua và cùng 7 lỗi cũ.
- SQL Server cô lập, lượt cuối trên code hoàn chỉnh: **10/10 qua**, gồm **9 ca migration** và ca voucher SQL chạy lại. Bao gồm dữ liệu lịch sử hợp lệ, số cột vật lý sau migration, bổ sung số buổi đã mua còn thiếu trong JSON, và rollback khi dữ liệu xung đột/sai kiểu hoặc giới hạn PT thay đổi ý nghĩa.
- Các ca hồi quy trainer/định danh dùng chung qua, bao gồm tải avatar, giữ nguyên khảo sát thể chất và tuyển lại PT.
- Luồng tạo tài khoản khách tại quầy trên SQLite: 25/25 ca qua.
- Bộ API mở rộng, không gồm WalkIn: **806/816 qua**. Mười lỗi trong lượt chung gồm bảy fixture migration cũ, một fixture consultation, một ca độ chính xác PDF trên SQLite và một ca deadlock voucher SQL. Ca voucher đã qua khi chạy lại riêng; không sửa luồng nghiệp vụ ngoài phạm vi để xử lý lỗi fixture cũ.
- Bảy fixture migration cũ dùng `LockerRules` với schema trước khi cột đó được thêm. Đã chạy lại một ca đại diện bằng binary trước khi thay đổi và gặp cùng lỗi. Lỗi consultation và PDF đã có trong báo cáo baseline. Vì vậy không khẳng định toàn bộ bộ kiểm thử đang xanh.
- EF xác nhận snapshot khớp model; không có thay đổi model còn thiếu migration. `git diff --check` không phát hiện lỗi whitespace.

Log xác minh được lưu ngoài thư mục Git của repo, tại `D:/SEP490_G144/Project/outputs/`: `schema-final-unit.log`, `schema-final-client.log`, `schema-final-api.log`, `schema-final-walkin.log`, `schema-verified-sql.log` và `schema-original-legacy-proof.log`.

8 file sửa tuyển dụng có sẵn đã được commit riêng: `e78b6f1 fix(trainer): improve recruitment filtering and eligibility checks`. Chưa push. Phần rút gọn schema được tách riêng khỏi commit này.
