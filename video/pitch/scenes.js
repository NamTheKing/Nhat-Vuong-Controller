// Pitch video storyboard. Each scene is one 8-second Google Flow clip, so each line of
// narration must fit in about 7 seconds (roughly 20 Vietnamese words). The Flow prompt for
// every scene is in STORYBOARD.md under the same id. `subtitle` is the English caption.
//
//   npm run voice:pitch            -> pitch/audio/<id>.wav + manifest.json

export const voice = {
  name: "Kore",
  style: "Đọc bằng tiếng Việt, giọng miền Bắc, tự tin, truyền cảm hứng, nhịp vừa phải, như đang thuyết trình gọi vốn cho một dự án khởi nghiệp:",
};

export const scenes = [
  { id: "01-hook", seconds: 8,
    subtitle: "Every evening, in classrooms with the lights off, the air conditioners keep running. No one turns them off, and no one even knows.",
    narration: "Mỗi buổi tối, trong những phòng học đã tắt đèn, điều hòa vẫn chạy. Không ai tắt, và cũng không ai biết." },
  { id: "02-problem", seconds: 8,
    subtitle: "The school pays for cooling empty rooms, while lecturers have to borrow a remote before every class.",
    narration: "Nhà trường trả tiền điện cho những căn phòng trống, còn giảng viên phải đi mượn điều khiển trước mỗi buổi học." },
  { id: "03-solution", seconds: 8,
    subtitle: "Introducing Nhat Vuong Controller: control every air conditioner on campus from your phone, following the class timetable.",
    narration: "Chúng tôi giới thiệu Nhat Vuong Controller: điều khiển mọi máy lạnh trong trường bằng điện thoại, theo đúng thời khóa biểu." },
  { id: "04-rights", seconds: 8,
    subtitle: "Teach in a room, and you can switch on its air conditioner. No sign-up, no remote.",
    narration: "Giảng viên có lớp ở phòng nào thì bật được máy lạnh phòng đó. Không cần đăng ký, không cần điều khiển." },
  { id: "05-autooff", seconds: 8,
    subtitle: "When the last class ends, the system switches off by itself. Before class starts, the room is already cool.",
    narration: "Hết tiết cuối, hệ thống tự tắt. Trước giờ học, phòng đã được làm mát sẵn." },
  { id: "06-maintenance", seconds: 8,
    subtitle: "When a unit fails, maintenance is alerted instantly, with the error code and the room.",
    narration: "Máy gặp sự cố, bộ phận bảo trì nhận cảnh báo ngay, kèm mã lỗi và vị trí phòng." },
  { id: "07-offline", seconds: 8,
    subtitle: "No internet? The app still works over the local network, protected by digital signatures.",
    narration: "Mất Internet? Ứng dụng vẫn điều khiển qua mạng nội bộ, được bảo vệ bằng chữ ký số." },
  { id: "08-traction", seconds: 8,
    subtitle: "27 features done, 111 automated tests, running on Android and Windows.",
    narration: "Hai mươi bảy tính năng đã hoàn thành, một trăm mười một bài kiểm thử, chạy trên Android và Windows." },
  { id: "09-ask", seconds: 8,
    subtitle: "Next step: a pilot in one campus building. Join us in turning every classroom into a smart classroom.",
    narration: "Bước tiếp theo: thí điểm tại một tòa nhà của trường. Hãy cùng chúng tôi biến mỗi phòng học thành phòng học thông minh." },
  { id: "10-close", seconds: 8,
    subtitle: "Nhat Vuong Controller. Cool when you need it, off when you don't.",
    narration: "Nhat Vuong Controller. Mát đúng lúc, tắt đúng giờ." },
];
