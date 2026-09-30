// The storyboard: one entry per scene. This file is the single source of truth for the
// narration — tts-gemini.mjs reads it to generate the voice-over, intro.js reads it to
// show subtitles and to know how long each scene lasts.
//
// id            matches a draw function in intro.js and the audio file audio/<id>.wav
// narration     what the Gemini voice reads (Vietnamese, product-facing like the UI)
// seconds       fallback length when no audio has been generated yet

export const voice = {
  // Prebuilt Gemini voices: Kore (firm), Puck (upbeat), Charon (informative), Aoede (breezy), ...
  name: "Kore",
  // Gemini TTS follows natural-language directions placed before the text.
  style: "Đọc bằng tiếng Việt, giọng miền Bắc, rõ ràng, thân thiện, tốc độ vừa phải, như người dẫn video giới thiệu sản phẩm:",
};

export const scenes = [
  {
    id: "intro",
    narration: "Xin chào! Đây là Nhat Vuong Controller, hệ thống điều khiển điều hòa cho toàn bộ khuôn viên trường đại học.",
    seconds: 6,
  },
  {
    id: "problem",
    narration: "Giờ học kết thúc, nhưng điều hòa vẫn chạy trong những phòng trống. Điện bị lãng phí mỗi ngày, và không ai biết phòng nào còn bật.",
    seconds: 7,
  },
  {
    id: "timetable",
    narration: "Với Nhat Vuong Controller, quyền điều khiển đến từ thời khóa biểu. Giảng viên có lớp trong phòng nào thì mở ứng dụng và bật điều hòa phòng đó, không cần đăng ký.",
    seconds: 9,
  },
  {
    id: "architecture",
    narration: "Ứng dụng gửi lệnh đến máy chủ. Máy chủ kiểm tra quyền, ghi nhật ký, rồi gửi lệnh qua MQTT tới mô-đun gắn trên từng máy lạnh, và nhận lại trạng thái thật.",
    seconds: 9,
  },
  {
    id: "autooff",
    narration: "Khi lớp cuối cùng kết thúc, hệ thống tự động tắt điều hòa. Nó còn làm mát trước giờ học và báo lỗi cho bộ phận bảo trì.",
    seconds: 8,
  },
  {
    id: "offline",
    narration: "Mất kết nối Internet? Ứng dụng vẫn điều khiển được qua mạng nội bộ, bằng quyền đã ký số và lịch học lưu sẵn trên thiết bị.",
    seconds: 8,
  },
  {
    id: "outro",
    narration: "Nhat Vuong Controller. Mát đúng lúc, tắt đúng giờ. Cảm ơn các bạn đã theo dõi!",
    seconds: 6,
  },
];
