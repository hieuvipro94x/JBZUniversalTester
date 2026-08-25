# V15.2.9 - Probe, relay va toc do TestWindow

Ngay sua: 2026-08-21

## Thay doi

- Sua nhan dien dau do D2XX trong stream Production khi board chi tra ve target-hit/active I/O, khong co source connection. Truoc do frame dang nay bi bo qua nen TestWindow khong hien IO khi cham que do vao mot so chan.
- Giu probe tach khoi TestEngine: frame dau do chi cap nhat UI, khong tao FAIL, khong tang san luong va khong kich relay.
- Cho phep vao Manual Relay khi chi dang scan nen Production sau khi da chon ma hang. Manual van bi khoa neu dang co chu ky test, dang Probe, dang xu ly PASS/FAIL/Master hoac dang loi thiet bi.
- Them cau hinh bat/tat va thu tu relay:
  - `JigEjectRelayEnabled`: bat/tat Relay 1 JIG trong chuoi PASS/FAIL/Master.
  - `PassMarkingRelayEnabled`: bat/tat Relay 2 MARKING trong chuoi PASS. FAIL va Master khong dung MARKING.
  - `PassJigRelayFirst`: chon thu tu PASS. `false` = MARKING ON/OFF roi JIG ON/OFF; `true` = JIG ON/OFF roi MARKING ON/OFF.
- Neu mot relay bi tat, phan mem van gui ALL RELAYS OFF va ghi log thay vi giu trang thai cu.
- Sau khi PASS da chot va relay da OFF, phan mem gui `RESET_CLEAR` ngay, reset engine UI ngay va chuyen khoi trang thai `DAT` sang `CHO THAO SAN PHAM` de o ket qua lon hien san sang nhanh hon.
- Bo delay `PostRelayRestartDelayMs` trong PASS path; delay nay khong con lam cham reset sau PASS.
- Ghi log `PASS_LATENCY T_PASS_TO_WAIT_REMOVE` va log operator `PASS -> SAN SANG/CHO THAO` de do thoi gian tu `DAT` sang trang thai cho tiep theo.
- Them `ProbeStateTracker` rieng: Probe state khong con la FaultGrid/source-of-truth; UI chi update khi Probe state doi.
- Production inline Probe khong con `return`/suppress ScanFrame. Frame nghi Probe van di tiep vao `TestEngine` de SHORT/WRONG that khong bi bo sot.
- `UseTestPointer=false` tat inline Probe classifier/display/interlock va clear Probe state dang co.
- Version phan mem tang tu 15.2.8 len 15.2.9.

## Huong dan su dung

1. Vao `CAI DAT PRODUCTION`.
2. Trong khoi `RELAY / BAO TRI`, chinh:
   - `R1 pulse`: thoi gian Relay 1 JIG giu ON.
   - `R2 pulse`: thoi gian Relay 2 MARKING giu ON.
   - `PASS R2 - R1`: delay an toan sau R2 truoc khi R1 chay.
   - `Bat Relay 1 JIG`: tat neu khong muon mo/day JIG tu dong.
   - `Bat Relay 2 PASS`: tat neu khong muon MARKING khi PASS.
   - `JIG truoc MARKING`: bat neu muon Relay 1 JIG chay truoc Relay 2 MARKING.
3. Bam `LUU`.
4. Muon test relay thu cong: bat `Manual Mode`, bam `LUU`, sau do dung khu vuc `MANUAL RELAY`.

## Kiem chung

- Self-test moi bao ve truong hop probe target-only.
- Self-test moi bao ve viec scan nen Production khong khoa Manual Relay menu.
- Self-test moi bao ve cau hinh tat Relay 2 MARKING nhung van cho Relay 1 JIG chay mot lan.
- Self-test moi bao ve cau hinh JIG-first chay Relay 1 truoc Relay 2.
- Self-test moi bao ve Probe candidate van duoc Production TestEngine xu ly.
- Self-test moi bao ve `UseTestPointer=false` tat Probe display/interlock.
- Build Release xac nhan cac thay doi compile dung trong V15.2.9.

## Can kiem tra hardware

- Cham que do vao cac IO tren bo D2XX that va xac nhan TestWindow hien dung IO/connector/pin.
- Chay PASS 20 lan voi `PassMarkingRelayEnabled=true`, sau do 20 lan voi `false`.
- Xac nhan Relay 1/Relay 2 vat ly ve OFF sau moi chu ky va sau khi bam Manual RESET.
- Do log `PASS -> SAN SANG/CHO THAO` tren may that de xac nhan thoi gian doi trang thai sau PASS va quan sat co treo UI hay khong.
