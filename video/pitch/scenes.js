// Pitch video storyboard. Each scene is one 8-second Google Flow clip, so each line of
// narration must fit in about 7 seconds (roughly 20 Vietnamese words). The Flow prompt for
// every scene is in STORYBOARD.md under the same id.
//
//   npm run voice:pitch            -> pitch/audio/<id>.wav + manifest.json

export const voice = {
  name: "Kore",
  style: "Đọc bằng tiếng Việt, giọng miền Bắc, tự tin, truyền cảm hứng, nhịp vừa phải, như đang thuyết trình gọi vốn cho một dự án khởi nghiệp:",
};

export const scenes = [
  { id: "01-hook", seconds: 8,
    narration: "Mỗi buổi tối, trong những phòng học đã tắt đèn, điều hòa vẫn chạy. Không ai tắt, và cũng không ai biết." },
  { id: "02-problem", seconds: 8,
    narration: "Nhà trường trả tiền điện cho những căn phòng trống, còn giảng viên phải đi mượn điều khiển trước mỗi buổi học." },
  { id: "03-solution", seconds: 8,
    narration: "Chúng tôi giới thiệu Nhat Vuong Controller: điều khiển mọi máy lạnh trong trường bằng điện thoại, theo đúng thời khóa biểu." },
  { id: "04-rights", seconds: 8,
    narration: "Giảng viên có lớp ở phòng nào thì bật được máy lạnh phòng đó. Không cần đăng ký, không cần điều khiển." },
  { id: "05-autooff", seconds: 8,
    narration: "Hết tiết cuối, hệ thống tự tắt. Trước giờ học, phòng đã được làm mát sẵn." },
  { id: "06-maintenance", seconds: 8,
    narration: "Máy gặp sự cố, bộ phận bảo trì nhận cảnh báo ngay, kèm mã lỗi và vị trí phòng." },
  { id: "07-offline", seconds: 8,
    narration: "Mất Internet? Ứng dụng vẫn điều khiển qua mạng nội bộ, được bảo vệ bằng chữ ký số." },
  { id: "08-traction", seconds: 8,
    narration: "Hai mươi bảy tính năng đã hoàn thành, một trăm mười một bài kiểm thử, chạy trên Android và Windows." },
  { id: "09-ask", seconds: 8,
    narration: "Bước tiếp theo: thí điểm tại một tòa nhà của trường. Hãy cùng chúng tôi biến mỗi phòng học thành phòng học thông minh." },
  { id: "10-close", seconds: 8,
    narration: "Nhat Vuong Controller. Mát đúng lúc, tắt đúng giờ." },
];
