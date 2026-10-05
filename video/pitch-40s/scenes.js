// 40-second pitch cut, timed to the edit in CapCut. Scenes are back to back: `seconds` is the
// length of each slot, and the voice must finish ~0.3 s before the slot ends.
//
//   0–8   hook        classroom, AC running        13–18  app: remote on/off
//   8–13  problem     corridor, ACs on             18–40  admin: incidents, notifications,
//                                                         naming the units, language switch
//
//   npm run voice:40s && npm run srt:40s && npm run track:40s

export const voice = {
  name: "Kore",
  style: "Đọc bằng tiếng Việt, giọng miền Bắc, tự tin, truyền cảm hứng, nhịp hơi nhanh và dứt khoát, như video giới thiệu sản phẩm công nghệ:",
};

export const scenes = [
  { id: "01-hook", seconds: 8,
    subtitle: "Every evening, the classrooms are empty, but the air conditioners keep running. No one turns them off, and no one knows.",
    narration: "Mỗi buổi tối, phòng học đã vắng, nhưng điều hòa vẫn chạy. Không ai tắt, và cũng không ai biết." },
  { id: "02-problem", seconds: 5,
    subtitle: "Empty rooms, AC still on. Electricity wasted every day.",
    narration: "Phòng trống, điều hòa vẫn bật. Tiền điện lãng phí mỗi ngày." },
  { id: "03-remote", seconds: 5,
    subtitle: "With Nhat Vuong Controller, switch any AC on or off remotely, in one tap.",
    narration: "Với Nhat Vuong Controller, bật tắt điều hòa từ xa chỉ bằng một chạm." },
  { id: "04-incidents", seconds: 6,
    subtitle: "When a unit breaks down, admins see right away which room and what fault.",
    narration: "Khi điều hòa gặp sự cố, quản trị viên thấy ngay phòng nào, lỗi gì." },
  { id: "05-notifications", seconds: 5,
    subtitle: "Every notification in one place, nothing missed.",
    narration: "Mọi thông báo được gom về một nơi, không bỏ sót." },
  { id: "06-setup", seconds: 6,
    subtitle: "Name and organise every air conditioner by room, in just a few taps.",
    narration: "Quản trị viên đặt tên, sắp xếp từng điều hòa theo phòng, chỉ vài thao tác." },
  { id: "07-language", seconds: 5,
    subtitle: "And switch between Vietnamese and English in one tap.",
    narration: "Và chuyển đổi giữa tiếng Việt và tiếng Anh chỉ trong một chạm." },
];
