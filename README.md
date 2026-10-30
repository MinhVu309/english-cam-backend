# english-cam-api — ASP.NET Core 8

Backend cho app English Cam. Kế hoạch: `../english-cam-app/docs/BACKEND_PLAN.md`.

## Đã deploy
**https://vuclaude309gmai-001-site1.etempurl.com** (SmarterASP.NET, SQL Server `site4now`). Swagger: `/swagger`, kiểm tra: `/health`.
Gói từ vựng cho app: mặc định app lấy từ `<API_URL>/api/pack/` (cùng máy chủ API), không cần đặt `PACK_URL`; sau mỗi lần Admin xuất bản, app tự báo có bản mới (xem `english-cam-app/docs/VOCAB_PACK.md`). Host miễn phí "ngủ" khi nhàn rỗi nên request đầu có thể chậm.
Deploy: `dotnet publish -c Release -o publish`, upload **nội dung** thư mục `publish/` bằng FileZilla (cần `appsettings.Production.json` có chuỗi kết nối, `Jwt:Key`, `Smtp:*`, `Google:ClientId`, `Seed:*`).

## Chạy local
```bash
docker compose up -d          # SQL Server 2022 (cổng 1434), chờ healthy ~15 giây
cp appsettings.example.json appsettings.json   # lần đầu
dotnet run                    # http://0.0.0.0:5080 , Swagger: /swagger , kiểm tra: /health
```
Lần chạy đầu tự tạo DB `EnglishCamDb` bằng EF migrations. Chưa cấu hình `Smtp:*` thì mã OTP in ra console.

Máy thật cùng Wi-Fi: gọi `http://<IP-Mac>:5080` (xem IP bằng `ipconfig getifaddr en0`).

## Trạng thái
- GĐ 0 xong: khung project, Docker SQL Server, `/health`.
- GĐ 1 xong (local): Identity 3 role (Admin/Parent/Child), JWT, đăng ký + xác minh email OTP, đăng nhập 2 bước (mật khẩu + OTP), quên/đặt lại mật khẩu, đăng xuất, `GET/PUT /api/auth/me`, Google login, seed Admin.
- GĐ 2 xong (local): hồ sơ con (CRUD), bé đăng nhập **nhiều thiết bị** (cùng máy phụ huynh hoặc mã liên kết 8 số), xem/thu hồi từng thiết bị, quản lý user cho Admin.
- GĐ 3 xong (local): từ vựng trong DB (seed 60 từ), CRUD từ/chủ đề/media cho Admin, bản nháp + xuất bản, `/api/vocab`, `/api/pack/*` tương thích `PackUpdater` của app. 51 test tự động.
- GĐ 4 xong: app Flutter nối API (đăng nhập, hồ sơ bé, nhập mã liên kết).
- GĐ 5 xong (local): game và tiến độ ở server — luật tính sao, câu hỏi thích ứng (Leitner), chuỗi ngày, huy hiệu, sự kiện idempotent, đồng bộ nhiều máy. 99 test tự động.
- GĐ 6 xong (local): dashboard phụ huynh (tóm tắt các bé, biểu đồ 7/30 ngày, tiến độ theo chủ đề, từ cần ôn, huy hiệu, hoạt động gần đây, xuất CSV). 126 test tự động.
- GĐ 7 xong (local): thống kê hệ thống cho Admin (`/api/admin/stats`), app thực thi giới hạn thời gian chơi và có màn quản trị. 131 test tự động. Chưa làm: báo cáo email tuần.

## Auth
| Việc | Gọi |
|---|---|
| Đăng ký | `POST /api/auth/register {email,password,fullName}` → mã 6 số về email → `verify-otp {userNameOrEmail,code}` → JWT |
| Đăng nhập | `POST /api/auth/login {userNameOrEmail,password}` → `{requiresOtp:true}` → `verify-otp` → JWT |
| Quên mật khẩu | `forgot-password {email}` → `reset-password {email,code,newPassword}` → JWT |
| Google | `google-login {idToken}` (cần `Google:ClientId` = Web client ID) |
| Đăng xuất | `POST /api/auth/logout` (có Bearer) |

- Kết quả đăng nhập: `{token, expiresAt, role, userId, fullName, email}`. JWT Parent/Admin sống 7 ngày (`Jwt:ParentExpireMinutes`).
- Đăng ký và Google luôn tạo **Parent**; chỉ Admin đổi được role (GĐ 2). Mật khẩu ≥ 8 ký tự, có chữ thường và số.
- Sai mật khẩu 5 lần → khoá 5 phút (HTTP 423). OTP sống 5 phút, thử tối đa 5 lần, gửi lại cách nhau ≥ 1 phút.
- Mỗi request có JWT đều được kiểm tra lại với DB: tài khoản bị khoá, đã đăng xuất (jti) hoặc đã đổi mật khẩu (SecurityStamp) thì token chết ngay.
- Admin seed: `Seed:AdminEmail` / `Seed:AdminPassword` (mặc định `admin@englishcam.local` / `Admin@123456`); đổi mật khẩu Admin đã có trong DB bằng `Seed:SyncAdminPassword=true`.

## Hồ sơ con và đăng nhập của bé
Phụ huynh (hoặc Admin) gọi với Bearer của mình; bé không thuộc về mình thì **404** (không lộ id có thật).

| Việc | Gọi |
|---|---|
| Danh sách / tạo / xem / sửa / xoá bé | `GET,POST /api/children` · `GET,PUT,DELETE /api/children/{id}` (tối đa 10 bé / phụ huynh; xoá bé xoá cả dữ liệu của bé) |
| Mục tiêu từ/ngày, giới hạn phút/ngày | `PUT /api/children/{id}/settings {dailyLimitMinutes?, dailyWordGoal}` |
| Bé dùng ngay trên máy phụ huynh | `POST /api/children/{id}/session {deviceId, deviceName?}` → token role **Child** |
| Thêm máy khác cho bé | `POST /api/children/{id}/link-code` → `{code (8 số), expiresAt}` (sống 10 phút, dùng 1 lần, mã mới huỷ mã cũ) → trên máy bé: `POST /api/auth/child-link {code, deviceId, deviceName?}` → token Child |
| Xem / thu hồi thiết bị | `GET /api/children/{id}/sessions` · `DELETE /api/children/{id}/sessions/{sid}` · `POST /api/children/{id}/revoke-sessions` |
| Bé xem hồ sơ + mục tiêu | `GET /api/child/me` (token Child) |

- Một bé đăng nhập được tối đa **10 thiết bị** cùng lúc; cùng thiết bị đăng nhập lại thì phiên cũ của máy đó bị thay.
- Token Child sống 30 ngày nhưng mỗi request đều kiểm tra phiên trong DB: thu hồi thiết bị, tắt hồ sơ (`isActive=false`) hoặc xoá bé có hiệu lực **ngay**.
- `child-link` giới hạn theo IP (`RateLimit:ChildLinkPerMinute`, mặc định 10/phút → HTTP 429) vì mã chỉ có 8 số và không gắn với bé cụ thể.
- Token Child chỉ gọi được `/api/child/*`, `/api/auth/me`, `/api/auth/logout` (đăng xuất = thu hồi phiên của máy đó). Gọi `/api/children`, `/api/admin/*` → 403.
- Tài khoản bé có email giả `child-…@child.englishcam.local` và không có mật khẩu; mọi luồng email/mật khẩu coi như không tồn tại.

## Góp ý nhận diện sai
App gửi khi bé/phụ huynh sửa kết quả nhận diện (chọn gợi ý khác hoặc gõ tên đúng). **Chỉ chữ, không có ảnh.**

| Việc | Gọi |
|---|---|
| Gửi / sửa một góp ý (Child, Parent, Admin) | `POST /api/feedback/recognition {id, predictedLabel, predictedProbability, top3[{label,probability}], correctWordId?, correctText, source: top3\|typed, modelSource?, vocabVersion?}` → `{id, updated}` |
| Xem (Admin) | `GET /api/admin/feedback?search=&source=&unknownOnly=&page=&pageSize=` · `GET /api/admin/feedback/summary` · `GET /api/admin/feedback/export.csv` · `DELETE /api/admin/feedback/{id}` |

- `id` do app sinh theo **từng ảnh chụp**: gửi lại hoặc đổi đáp án trên cùng ảnh thì cập nhật cùng một dòng (`updated: true`); `id` của người khác → 409.
- Chữ được làm sạch (bỏ ký tự điều khiển, gộp khoảng trắng, 1 đến 60 ký tự); `correctWordId` không có trong từ vựng thì bỏ (vẫn lưu `correctText`). Mỗi tài khoản tối đa **100 góp ý mới/ngày** (429), sửa góp ý cũ không tính.
- `summary`: tổng, 7 ngày, số lần model đoán đúng, 10 cặp "đoán X nhưng đúng là Y" hay gặp nhất, và 10 từ người dùng gõ mà từ vựng chưa có (gợi ý nên thêm từ nào).
- Xoá tài khoản (bé hoặc phụ huynh) thì xoá luôn góp ý của tài khoản đó.

## Thống kê hệ thống (Admin)
`GET /api/admin/stats` → `{users, children, vocab, activity, hardestWords, generatedAt}`: số Admin/Phụ huynh/tài khoản bé, số tài khoản bị khoá (chỉ Admin/Phụ huynh), phụ huynh mới 7 ngày; số hồ sơ bé, bé hoạt động hôm nay / 7 ngày, máy đang liên kết (không tính máy đã thu hồi hay hết hạn); phiên bản và số từ/chủ đề **đã xuất bản**; số ván, phút chơi 7 ngày, tổng sao; tối đa 10 từ bị sai nhiều nhất (tỉ lệ sai cộng dồn mọi bé, chỉ xét từ có ít nhất 5 lượt trả lời và đã xuất bản). Ngày chia theo `LocalTime`.

## Quản lý người dùng (Admin)
`GET /api/admin/users?search=&role=&page=&pageSize=` · `GET,PUT,DELETE /api/admin/users/{id}` · `POST /api/admin/users {email,password,fullName,role}`
- `role` chỉ là `Admin` hoặc `Parent`. Danh sách mặc định ẩn tài khoản bé (`?role=Child` để xem).
- Đổi role → người đó bị đăng xuất mọi máy (SecurityStamp đổi). Khoá tài khoản → có hiệu lực ngay, đăng nhập trả 403.
- Không tự đổi role / tự khoá / tự xoá mình. Xoá phụ huynh xoá luôn các bé của họ.

## Từ vựng
**Phát cho app (công khai, không cần đăng nhập):**

| Gọi | Trả |
|---|---|
| `GET /api/vocab` | `vocab.json` đúng schema `assets/vocab/vocab.json` của app (có `ETag`, gửi `If-None-Match` được 304) |
| `GET /api/vocab/version` | `{version, notes, createdAt}` của bản đang phát |
| `GET /api/pack/manifest.json`, `/api/pack/vocab.json`, `/api/pack/{path}` | "Gói từ vựng" cho `PackUpdater` và `PackWatcher` của app (mặc định app dùng `<API_URL>/api/pack/`; chỉ cần đặt `PACK_URL` khi muốn trỏ chỗ khác). `manifest.json` có `version` tăng mỗi lần xuất bản nên app so số này để biết có bản mới |

**Quản lý (Admin):**

| Việc | Gọi |
|---|---|
| Từ | `GET,POST /api/admin/words` · `GET,PUT,DELETE /api/admin/words/{id}` · `POST …/{id}/restore` (lọc `search`, `category`, `hasLabel`, `includeDeleted`, phân trang) |
| Chủ đề | `GET,POST /api/admin/categories` · `PUT,DELETE …/{id}` · `POST …/{id}/restore` (không xoá được chủ đề còn từ) |
| Âm thanh / ảnh | `POST /api/admin/media` (multipart: `Path`, `File`) · `GET /api/admin/media` · `DELETE /api/admin/media?path=` |
| Nháp → phát hành | `GET /api/admin/vocab/status` · `GET …/validate` · `POST …/publish {notes}` · `GET …/releases` |
| Nhãn của model | `GET /api/admin/vocab/model-labels` |

- **Sửa là sửa nháp.** App chỉ thấy sau khi `publish`: server đóng băng `vocab.json` + `manifest.json` (sha256/size tính trên đúng byte sẽ phát, vì `PackUpdater` bỏ cả lần cập nhật nếu sai một byte). Không có gì thay đổi → 409. Nháp không hợp lệ → 400 kèm danh sách lỗi.
- **Nhãn model:** `label` chỉ được là một trong 24 dòng của `models/labels.txt` (bản sao ở `Data/Seed/model_labels.txt`; có test so với file thật), mỗi nhãn một từ. Từ không có nhãn thì `hunt` luôn false. **Nâng model → cập nhật cả hai nơi.**
- **Media:** `audio/words/*.mp3`, `audio/examples/*.mp3` (≤ 1 MB), `images/*.png|jpg|jpeg|webp` (≤ 2 MB); kiểm tra cả byte đầu của file chứ không tin phần mở rộng. Lưu theo mã băm nội dung (`Media:Root`, mặc định `wwwroot/media`), nên tải đè cùng đường dẫn không đổi bản đã phát cho tới lần xuất bản sau.
- **Seed:** DB trống thì nạp `Data/Seed/vocab.json` (bản đóng gói trong app) thành bản 1, giữ đường dẫn `assets/…` nên 60 từ gốc không cần tải âm thanh (manifest chỉ có `vocab.json`). Từ mới của Admin dùng đường dẫn tương đối.
- Xoá từ/chủ đề là xoá mềm (tiến độ của bé tham chiếu id từ).

## Game và tiến độ của bé
Token role **Child**. Server là nơi duy nhất tính sao/huy hiệu; app chỉ gửi *đáp án*, không gửi số sao.

| Việc | Gọi |
|---|---|
| Câu hỏi "Nghe & chọn hình" | `GET /api/games/match/questions?count=8` → `{vocabVersion, questions:[{wordId, optionIds[4]}]}` — ưu tiên từ bé hay sai/chưa gặp, chỉ từ bản **đã xuất bản** |
| Gợi ý vật cần tìm | `GET /api/games/hunt/targets?count=5` (chỉ từ có nhãn model và `hunt=true`) |
| Ghi một ván | `POST /api/games/sessions {id, gameType: match\|hunt, startedAt, durationSec, answers:[{wordId, correct, attempts, via?}]}` → `{starsEarned, duplicate, newBadges[], progress}` |
| Sự kiện tiến độ (tối đa 200/lô) | `POST /api/progress/events {events:[{id, type: learned\|scan\|screenTime, wordId?, learned?, seconds?, at}]}` → `{applied, duplicates, ignored, newBadges[], progress}` |
| Trạng thái hiện tại | `GET /api/progress/me` → sao, ván, chuỗi ngày, từ đã học, huy hiệu, mục tiêu/giới hạn của bố mẹ, hoạt động hôm nay |
| Huy hiệu (kèm đã mở chưa) | `GET /api/games/badges` · Admin CRUD: `/api/admin/badges` |
| Bố mẹ xoá tiến độ của bé | `DELETE /api/children/{id}/progress` |

- **Luật sao** (khớp app): *Match* — mỗi câu đúng **ngay lần chọn đầu** = 1 sao, "hoàn hảo" = đúng hết và ≥ 5 câu; *Hunt* — model nhận ra = 2 sao, bố mẹ xác nhận tay (`via: "parent"`) = 1 sao; Tìm vật không tính là "ván". Khai gian `starsEarned` trong thân yêu cầu bị bỏ qua.
- **Idempotent:** ván và sự kiện có `id` do app sinh; gửi lại trả `duplicate`/`duplicates`, không cộng đôi (kể cả hai máy gửi cùng lúc). Từ lạ/loại lạ bị bỏ qua (`ignored`), không làm hỏng cả lô.
- **Nhiều máy:** sao/ván/huy hiệu cộng dồn trên server; "đã học" lấy hợp, bật/tắt cùng một từ thì bản có `at` **mới hơn thắng** dù máy nào đồng bộ trước.
- **Leitner:** đúng ngay lần đầu → lên hộp (tối đa 5, hẹn ôn lại 1/2/5/10 ngày); sai → về hộp 1, hỏi lại ngay. Chọn từ theo trọng số (chưa gặp 4; hộp 1..5 = 5,4,3,2,1; chưa tới hạn ×0,4).
- **Chuỗi ngày:** tính theo giờ UTC+7 (`App:TimeZoneOffsetHours`); học = chơi ván, đánh dấu đã học, nhận ra vật (chỉ mở app thì không tính). Bỏ lỡ một ngày thì chuỗi hiện tại về 0, kỷ lục `bestStreak` giữ. Huy hiệu `streak_3`, `streak_7` dựa trên kỷ lục.
- **Chống lạm dụng:** tối đa 20 đáp án/ván (5 với Hunt), 60 ván/giờ, mỗi sự kiện screenTime ≤ 1 giờ và ≤ 24 giờ/ngày; giờ do app gửi chỉ được tin trong khoảng [−30 ngày, +5 phút], ngoài khoảng dùng giờ máy chủ.
- Huy hiệu seed từ `Data/Seed/badges.json` (8 huy hiệu của app + `streak_3`, `streak_7`).

## Dashboard cho phụ huynh
Token role **Parent** (hoặc Admin); bé không phải của mình → **404** như không tồn tại. Chỉ đọc.

| Việc | Gọi |
|---|---|
| Thẻ tóm tắt các bé của mình | `GET /api/parent/overview` → mỗi bé: từ đã học/tổng, sao, chuỗi ngày, phút hôm nay, số thiết bị |
| Dashboard một bé | `GET /api/parent/children/{id}/dashboard?days=7` (1–90) |
| Xuất CSV | `GET /api/parent/children/{id}/export.csv?kind=daily&days=30` · `?kind=words` (UTF-8 có BOM để Excel đọc đúng tiếng Việt) |

Dashboard gồm: tổng (từ đã học/tổng, sao, ván, hoàn hảo, đã tìm, chuỗi hiện tại/kỷ lục, tổng phút, độ chính xác), hôm nay (giây, từ mới, ván, nhận diện, đạt mục tiêu chưa), từng ngày trong khoảng (đủ cả ngày không học, ngày cuối là hôm nay), tiến độ theo chủ đề, tối đa 5 từ cần ôn, 5 huy hiệu mới nhất (+ đã mở/tổng), 10 hoạt động gần đây.

- **Đếm trên bản đã xuất bản:** "tổng số từ" và "đã học" chỉ tính từ có trong bản từ vựng đang phát, nên tử số không bao giờ vượt mẫu số kể cả khi Admin xoá từ.
- **Từ cần ôn:** từ có ít nhất 1 lần sai và gặp ≥ 2 lần, xếp theo tỉ lệ sai rồi số lần sai. **Độ chính xác** = số câu đúng ngay lần đầu / tổng số câu trong các ván "Nghe & chọn hình" của khoảng đang xem (null nếu chưa có ván).
- **Ngày** chia theo giờ UTC+7 (`App:TimeZoneOffsetHours`), cùng cách với chuỗi ngày (`LocalTime`).
- **CSV an toàn:** ô bắt đầu bằng `= + - @` được thêm dấu `'` để Excel/Sheets không coi là công thức (chống CSV injection từ tên từ do Admin nhập).

## Test
```bash
docker compose up -d
cd tests/EnglishCam.Api.Tests && dotnet test     # 149 test, ~31 giây
```
Test chạy API thật trên SQL Server Docker với database tạm `EnglishCamTest_<guid>` (xoá khi xong), cấu hình bằng biến môi trường nên không đụng DB dev. Thư OTP được thay bằng bản giả để đọc mã. Ngoài ra đã chạy thử **`PackUpdater` thật của app** (Dart) với API này: tải đủ manifest + mp3 + png, sha256 đúng, `VocabRepository` nạp được 61 từ, lần hai nhận ra không có gì mới. Test chống truy cập chéo đã được kiểm chứng bằng cách bỏ tạm kiểm tra quyền sở hữu: test chuyển đỏ.
