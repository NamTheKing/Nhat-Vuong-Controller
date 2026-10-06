// Voice-over for a 25-second screen recording of the Windows app (signed in as admin).
// `start` is where each screen begins in the recording; `seconds` is how long it is shown.
// make-demo-video.mjs freezes the last frame of a segment when its line runs longer.
//
//   node tts-gemini.mjs --scenes app-demo/scenes.js
//   node make-demo-video.mjs app-demo/scenes.js <recording.mp4> <output.mp4>

export const voice = {
  name: "Kore",
  style: "Đọc bằng tiếng Việt, giọng miền Bắc, ấm áp, truyền cảm, rõ ràng, nhịp hơi nhanh và tự tin, như video giới thiệu sản phẩm công nghệ:",
};

export const scenes = [
  { id: "1-devices", start: 0, seconds: 2.65,
    narration: "Đây là Nhat Vuong Controller. Mọi điều hòa trong trường, gói gọn trên một màn hình." },
  { id: "2-detail", start: 3.0, seconds: 4.2,
    narration: "Chạm vào một máy để xem trạng thái thật, rồi chỉnh nhiệt độ, chế độ và tốc độ quạt." },
  { id: "3-incidents", start: 7.55, seconds: 3.2,
    narration: "Mục Sự cố hiện ngay các lỗi đang mở: máy chạy quá lâu, lỗi cảm biến, hay mất kết nối." },
  { id: "4-admin", start: 10.95, seconds: 4.25,
    narration: "Trang quản trị gom đủ công cụ: phòng học, người dùng, phân quyền, nhật ký và báo cáo." },
  { id: "5-rooms", start: 16.15, seconds: 2.75,
    narration: "Quản lý tòa nhà, phòng học và số điều hòa trong từng phòng." },
  { id: "6-notifications", start: 19.2, seconds: 3.45,
    narration: "Cảnh báo được gửi thẳng đến quản trị viên, không bỏ sót." },
  { id: "7-language", start: 22.9, seconds: 2.4,
    narration: "Và chuyển giữa tiếng Việt, tiếng Anh chỉ trong một chạm." },
];
