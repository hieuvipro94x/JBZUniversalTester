# BÁO CÁO CODEX REVIEW — JBZUniversalTester V15.2.0
## RELAY TIMING TÁCH RIÊNG + SAFE PULSE + FAIL EJECT 1 LẦN

## 1. Mục tiêu thay đổi

V15.2.0 tập trung vào an toàn relay và làm rõ cấu hình thời gian:

- Relay 1 = JIG EJECT / MỞ-ĐẨY JIG.
- Relay 2 = MARKING PASS.
- Hai relay có thời gian pulse độc lập.
- Có delay độc lập giữa R2 OFF và R1 ON trong chu trình PASS.
- Mọi pulse phải chỉ bật đúng một lần rồi cưỡng bức OFF.
- FAIL sau khi người vận hành XÁC NHẬN: chỉ R1 pulse một lần, R2 tuyệt đối không chạy.
- Nếu OFF thất bại thì dừng workflow, không cho relay tiếp theo chạy.
- Không được để relay kế thừa trạng thái ON từ lần trước.

## 2. Settings mới

Thay dòng cũ:

```text
R1 JIG, R2 MARKING (ms) [250,250]
```

bằng 3 trường riêng:

```text
R1 JIG - thời gian bật (ms)      [250]
R2 MARKING - thời gian bật (ms) [250]
PASS: chờ R2 → R1 (ms)          [430]
```

Property:

```csharp
Relay1JigPulseMs
Relay2MarkingPulseMs
PassMarkingToJigDelayMs
```

Giới hạn:
- R1: 50..5000 ms.
- R2: 50..5000 ms.
- R2→R1: 0..5000 ms.

`StampDelay` vẫn được giữ chỉ để tương thích cấu hình cũ.

## 3. Migration cấu hình

Nếu `production.settings.json` cũ chưa có `Relay1JigPulseMs`/`Relay2MarkingPulseMs`, V15.2 đọc `StampDelay="R1,R2"` và migrate.

Legacy `UniversalTester.cfg`:
- đọc các key mới nếu có;
- nếu chưa có thì fallback `StampDelayMs`;
- khi save vẫn ghi thêm `StampDelayMs=R1,R2` để tương thích bản cũ.

## 4. FAIL — yêu cầu bắt buộc

```text
FAIL
→ hiển thị "KIỂM TRA MẠCH KHÔNG ĐẠT"
→ operator XÁC NHẬN
→ ALL RELAYS OFF
→ R1 JIG ON đúng một lần
→ delay Relay1JigPulseMs
→ ALL RELAYS OFF
→ chờ tháo toàn bộ sản phẩm
→ re-arm
```

R2 MARKING phải OFF trong toàn bộ FAIL.

Nếu R1 pulse bị cancellation hoặc exception:
- `finally` vẫn chạy safe-OFF;
- safe-OFF retry tối đa 3 lần;
- nếu vẫn không OFF được thì workflow báo lỗi và không chạy tiếp.

## 5. PASS — yêu cầu bắt buộc

```text
PASS
→ ALL RELAYS OFF
→ R2 MARKING ON đúng một lần
→ delay Relay2MarkingPulseMs
→ ALL RELAYS OFF
→ delay PassMarkingToJigDelayMs
→ R1 JIG ON đúng một lần
→ delay Relay1JigPulseMs
→ ALL RELAYS OFF
→ chờ tháo sản phẩm
```

Không được để R2 còn ON khi R1 bật.

## 6. Manual Relay

Nút:
- `RELAY 1 - MỞ JIG (PULSE 1 LẦN)`
- `RELAY 2 - MARKING (PULSE 1 LẦN)`

phải dùng cùng API safe pulse của TestEngine, không tự viết flow riêng trong ViewModel.

## 7. Safe pulse implementation

Mỗi pulse:
1. acquire semaphore;
2. cưỡng bức ALL OFF trước;
3. bật đúng relay;
4. delay;
5. `finally`: cưỡng bức ALL OFF;
6. release semaphore.

Safe-OFF retry 3 lần. Nếu safe-OFF thất bại, workflow ném lỗi và không chạy relay tiếp theo.

## 8. Test bắt buộc

- 20 FAIL liên tục: mỗi lần đúng 1 pulse R1, R2 không ON.
- 20 PASS liên tục: mỗi lần R2 đúng 1 pulse, OFF, delay, R1 đúng 1 pulse, OFF.
- 20 Manual R1 và R2.
- spam click.
- rút USB giữa pulse.
- cancel cycle trong pulse.
- đóng TestWindow giữa pulse.

## 9. Lưu ý phần cứng

Nếu mất USB/nguồn đúng khi relay vật lý đang ON, phần mềm có thể cố gửi OFF nhưng không thể đảm bảo relay vật lý nhả nếu board/firmware không còn nhận lệnh. Cần test bo thật và, nếu phần cứng yêu cầu fail-safe tuyệt đối, firmware/relay driver nên có watchdog hoặc default-OFF khi mất host.

## 10. Kết luận

Nguyên tắc V15.2:

> Mỗi thao tác relay = OFF trước → ON một lần → OFF bắt buộc.

> Nếu không đưa được relay về OFF, workflow dừng và báo lỗi; không tiếp tục relay kế tiếp.
