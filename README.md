### ⚠️ IMPORTANT NOTICE / DISCLAIMER

**Original Author:** EchoStarz
**Original Repository:** CompoundingPerf
**Original Link:** https://forge.sp-tarkov.com/mod/2415/compoundingperf
**License:** MIT
**This Port By:** R_F (danyhappy564-cmyk) — unofficial, AI-assisted port. Not affiliated with or endorsed by the original author.

1. **Reflection & Take-Downs:** I deeply reflect on the ECOT incident. As an AI-assisted "vibe coder," I will immediately delete files if the original authors ask.
2. **No Re-Distribution:** These ported builds are unverified, temporary fixes. Please do NOT re-upload or share them anywhere else.
3. **Do Not Pester Original Authors:** Never report bugs or pester original modders regarding issues from my unofficial ports.
4. **Full Credit & Respect:** I will always credit original creators on GitHub and prioritize their decisions above all else.
5. **Support Original Creators:** Instead of using my ports, please visit the original authors' Forge pages to leave kind words or tips.

---

# CompoundingPerf (SPT 4.1)

> **원작자** — **EchoStarz** (MIT, `LICENSE` 참고)
>
> 이 레포는 위 모드의 **SPT 4.1 포팅**입니다. 4.1에서 이 모드가 쓰던 확장 지점이 통째로
> 사라져서, 단순 리네임이 아니라 **기능별로 재검증하고 전달 방식을 갈아엎은** 작업입니다.
> 기능 11개 중 6개가 빠지고 1개가 새로 들어갔습니다. 2.1에서 1개(S16)와 디버그 로그가 더 들어갔습니다.

현재 기준 **SPT 4.1.5**. 최적화는 전부 **서버 쪽**에서 일어납니다. 게임 쪽에는 **F12 설정 화면용 플러그인 하나**만
올라가고(2.2부터), 이 플러그인은 설정을 서버에 전달하는 일만 합니다 — 게임 성능에는 영향이 없습니다.
게임 쪽 메모리·끊김은 RAM 클리너(zzap) 몫이고, 이 모드는 **SPT 서버 프로세스** 쪽만 다룹니다.

## 변경 이력 (KST)

### v2.2.2 추가 — 2026-10-04 21:25 (SPT 런처 '모드 페이지'에 등록)
- **런처 / SPT 웹 패널의 '모드 페이지' 목록에 'CompoundingPerf'** 가 뜹니다(`IModBlazorMetadata`, 주소 `/compoundingperf`).
  서버가 직접 그리는 페이지라 **게임을 켜지 않아도** 서버 상태(메모리·마지막 레이드 후 정리)를 보고 설정 18개를 바로 바꿀 수 있습니다(F12와 같은 경로: 즉시 적용 + config.json 저장).
  한/영 전환 버튼, 검색, 기본값 버튼. 처음 언어는 게임 쪽 F12 플러그인의 언어 설정을 따릅니다.
- 보기는 SPT 웹 패널 로그인 규칙을 따르고(이 PC에서는 기본으로 바로 열림), 바꾸기는 관리자만(SPT 설정 편집기와 같은 규칙). 변경 요청은 이 페이지가 보낸 것만 받음.
- 설정 이름·설명(한/영)을 서버와 F12가 같이 쓰는 `SettingsCatalog.cs` 한 곳으로 모음(두 화면 문구가 항상 같음).
- 확인: SPT 웹과 같은 방식으로 컨트롤러를 등록한 ASP.NET 호스트에서 페이지·상태·변경·범위 보정·잘못된 값·관리자 아님/헤더 없음 거부, Chromium 렌더(한/영/폰). **실제 런처 목록 표시는 미확인.**

### v2.2.2 추가 — 2026-10-04 20:35 (한국어/English + RAM 클리너 웹 페이지 연동, 아직 배포 전이라 같은 버전에 합침)
- **F12 한/영 전환:** F12 → `00. 상태 · 언어` → `언어 (Language)`. 분류·이름·설명·상태 줄이 같이 바뀝니다(F12는 창을 닫았다 열면 적용).
  서버가 보내는 '마지막 레이드 후 정리' 문구도 영어판을 같이 보냅니다(`LastCleanupEn`, 예전 서버 모드면 한국어만).
- **RAM 클리너(zzap) 웹 페이지의 '서버 최적화' 탭:** RAM 클리너 2.12.0을 같이 쓰면 브라우저 `http://127.0.0.1:6977/server` 에서
  이 모드의 F12 항목 전부와 서버 상태를 보고 바꿀 수 있습니다. 바꾸는 방식은 F12와 똑같습니다(0.8초 뒤 서버 적용·config.json 저장).
  RAM 클리너의 언어를 바꾸면 이 모드의 언어도 따라갑니다.
- 압축 수준·S15 방식의 선택지 이름을 두 언어로 바꿨습니다(예: `빠름 · Fastest (추천)`). 값은 서버에서 다시 읽어 오므로 예전 설정이 사라지지 않습니다.
- 다른 플러그인이 읽는 창구 `CompoundingPerf.Client.StatusBridge`(연결 여부·상태 줄·마지막 적용 결과·다시 읽기) 추가. 두 모드는 서로를 참조하지 않습니다(리플렉션).

### 문서 — 2026-10-04 15:10 (SAIN·ORBIT 대조, 코드 변경 없음)
- SAIN(zzap `9f411b7`)·ORBIT(zzap `362016d`)와 대조: **호환 문제 없음.** 자세한 건 아래 "호환성".

### v2.2.2 — 2026-10-04 15:08 (RAM 클리너와 같이/따로 쓰는 경우 정리)
- **F12 플러그인만 깔고 서버 모드가 없을 때**: 전에는 10초마다 서버에 계속 물어봤습니다 → 이제 상태 칸에
  "서버 모드가 없습니다"라고 알리고 **5분마다**만 확인합니다(`서버에서 다시 읽기` 버튼으로 바로 확인 가능).
- F12 `레이드 끝나고 기다릴 시간` 설명에 RAM 클리너와 겹치지 않게 맞추는 법을 넣었습니다.
- README에 "RAM 클리너와 같이 / 따로 쓸 때" 절 추가.

### v2.2.1 — 2026-10-04 15:06 (APBS·ABPS 대조)
- **S12 복사 줄임**: 봇 장비 설정 사본을 "값을 실제로 고치는 곳"(바닐라 장비 생성, 야간 보정)에서만 만들도록 바꿨습니다.
  전에는 읽기만 하는 곳까지 매번 복사해서, 무기 부착물 생성(부착물 칸마다 반복)과 **APBS의 장비 생성기**에서 봇 한 명당 수십 번 쓸모없는 복사가 있었습니다.
  이제 바닐라 봇은 1번, **APBS가 만드는 봇은 0번**입니다. 안전성은 같습니다(공용 설정에 쓰는 곳이 없음).
- **디버그 로그**: 시작할 때 APBS·ABPS가 깔려 있는지 표시하고, `[bots]` 시간에 APBS 장비 생성이 포함된다는 것과
  ABPS·APBS의 레이드 시작/끝 처리 시간은 `[requests]` 표에서 봐야 한다는 것을 적습니다.
- 확인 결과 **겹치는 문제 없음**: APBS·ABPS는 레이드 시작/끝 주소에 자기 처리를 따로 붙이고(S15·S16과 별개), GC를 직접 부르지 않으며,
  ABPS의 레이드 끝 작업은 종료 요청 안에서 끝나서 S16(30초 뒤 + 조용할 때)과 겹치지 않습니다.

### v2.2.0 — 2026-10-04 15:00
- **F12 설정 화면 신규**: 게임 안에서 F12 → `CompoundingPerf.Client` 에서 모든 기능을 한글로 보고 바꿀 수 있습니다.
  **바꾸는 즉시(0.8초 뒤) 서버에 적용**되고, 서버의 `config.json` 에도 저장돼서 다음에 서버를 켜도 유지됩니다.
  맨 위 `00. 상태` 에 서버 연결 여부, 서버 메모리, 마지막 레이드 후 정리 결과가 나옵니다.
- **디버그 로그를 F12에서 바로 켜고 끌 수 있음** (재시작 불필요). 켜면 상태 칸에 로그 파일 경로가 나옵니다.
- **릴리스 zip 구성 변경**: 이제 `SPT_Runtime\…` 와 `BepInEx\…` 폴더가 들어 있어서 **`E:\SPT 4.1` 에 바로 풀면** 됩니다.
- S11(저장 건너뛰기)을 실행 중에 켜면, 켜기 전 요청은 추적되지 않았으므로 첫 저장은 무조건 진짜로 하도록 했습니다.

### v2.1.0 — 2026-10-04 13:50
- **S16 PostRaidCleanup 신규 (기본 켜짐)**: 레이드가 끝나고 서버가 한가해지면(결과 화면 보는 동안)
  서버 메모리를 정리해 **윈도우에 돌려줍니다.** 이유는 아래 "S16이 왜 필요한가" 참고.
- **디버그 로그 신규 (기본 꺼짐)**: `config.json` 의 `Debug.Enabled: true` 로 켜면
  `SPT_Runtime\user\logs\CompoundingPerf\` 에 별도 로그 파일이 생깁니다.
  패치가 제대로 걸렸는지(셀프 체크), 레이드마다 서버 메모리·GC, 느린 요청, 봇 생성 시간이 남습니다.
- **고침**: 2.0의 `Telemetry` 설정은 켜도 아무 일도 안 했습니다(연결된 코드가 없었음) → 디버그 로그로 대체하고 config에서 뺐습니다.
  옛 config에 남아 있어도 무시되니 문제없습니다.
- **고침**: 벤치 빌드 전용 기록기가 S8·S11 카운터를 틀린 이름으로 읽어서 항상 0이 나오던 것.
- **고침(문서)**: README의 설치 경로(`SPT\user\mods` → `SPT_Runtime\user\mods`), 그리고
  README·config의 "설정이 재시작 없이 바로 적용된다"는 틀린 설명(config.json은 시작 때 한 번만 읽음 → **서버 재시작 필요**).

### v2.0.0 — SPT 4.1 포팅
- 원작 1.x를 SPT 4.1.5로 포팅. 4.1이 직접 해결한 기능 5개 폐기, S15 신규. 상세는 `CHANGELOG.md`.

---

## 뭐 하는 모드냐

서버 쪽 작은 최적화들을 하나의 config로 묶어 켜고 끄는 모드입니다. 기능마다 개별 스위치가
있고, 원작이 세운 규칙이 하나 있습니다: **비용이 구조적으로 한정되지 않는 기능은 넣지
않는다.** 모드 잔뜩 깔린 환경에서 터질 여지가 있으면 아예 안 넣습니다.

| 기능 | 기본값 | 하는 일 |
|---|---|---|
| **S15 RaidStartGc** | **켜짐** | **2.0에서 신규.** 레이드 시작 응답 직전에 `GC.Collect(MaxGeneration, Aggressive, blocking, compacting)` — .NET에서 제일 비싼 GC — 가 **요청 경로 안에서** 돕니다. 로딩 화면에서 서버가 힙 전체를 압축하는 걸 매 레이드마다 기다리는 셈. 기본값 `Background` 는 gen-2 수집은 그대로 요청하되 **논블로킹·논컴팩팅** 으로 바꿔서 응답을 안 붙잡습니다 |
| **S8 RagfairCalmUpdates** | 켜짐 | 플리 매물 만료 처리 끝에 강제 blocking·compacting 풀 GC가 붙어 있습니다. 그 **강제 수집 한 번만** 제거하고 만료 시퀀스 자체는 안 건드립니다 |
| **S9 FastCompression** | 켜짐 | 모든 응답을 zlib `SmallestSize`(제일 느림)로 압축합니다. `Fastest` 는 CPU가 몇 배 싸고 대신 몇 % 커지는데, 어차피 localhost/LAN만 건너갑니다. 버퍼 경로와 스트리밍 경로 둘 다 적용 |
| **S12 IsolatedBotRandomisation** | 켜짐 | **성능이 아니라 버그 수정.** 야간 레이드 장비 수정치가 **공유** 봇 config에 그대로 써집니다 → 봇 생성할 때마다 누적되고, 재시작 전까지 낮 레이드에도 남고, 병렬 생성에서 레이스가 납니다. 값을 고치는 바닐라 장비 생성 단계에만 사본을 주면 의도대로 정확히 한 번만 적용됩니다 (2.2.1부터 읽기만 하는 곳·APBS는 복사 안 함) |
| **S13 CalmNotifier** | 켜짐 | `/notify` 롱폴이 15초 예산 동안 스레드풀 스레드를 **접속자 수만큼** 붙잡고 있습니다. 체크 사이에 스레드를 놓아주도록 바꿉니다. FIKA 호스트에서 제일 값어치 있음 |
| **S16 PostRaidCleanup** | **켜짐** | **2.1에서 신규.** 레이드 종료 30초 뒤, 서버에 플레이어 요청이 3초간 없을 때 강력한 GC 한 번(압축 + 윈도우에 메모리 반납). 서버가 붙잡은 메모리가 512MB 미만이면 건너뜁니다. 할 때마다 서버 콘솔에 `post-raid cleanup: ... → ... MB` 한 줄 |
| **S11 SaveDirtyTracking** | **꺼짐** | 세션이 확실히 깨끗할 때 주기 저장을 통째로 건너뜁니다 (바닐라는 idle이어도 매 틱 프로필 전체를 직렬화 + MD5). 아래 "왜 기본으로 꺼져 있나" 참고 |

### 왜 S11만 기본으로 꺼져 있나

이 모드에서 **유일하게 바닐라 호출을 막는** 기능이기 때문입니다. 나머지는 전부 상수를
바꾸거나, 호출 하나를 동등한 것으로 돌리거나, 사본을 얹는 식이라 **잘못돼도 최악이
"최적화가 아무 일도 안 함"** 입니다. S11은 최악이 **"프로필 변경이 안 써짐"** 입니다.

게다가 이득이 제일 작습니다. 메뉴에서 가만히 있을 때만 효과가 있고, 레이드 중엔 요청이
오가니 어차피 dirty로 표시돼서 안 걸립니다. **위험은 제일 크고 이득은 제일 작아서**
opt-in으로 뺐습니다. 쓰려면 `config.json` 에서 `SaveDirtyTracking.Enabled: true`.

### S16이 왜 필요한가

바닐라는 레이드 **시작** 때 가장 강한 GC(`Aggressive`)를 돌립니다. 느린 대신 남는 메모리를
윈도우에 돌려줍니다. S15가 로딩을 빠르게 하려고 이걸 가벼운 백그라운드 GC로 바꿨는데,
그 대가로 **서버가 레이드 사이에 메모리를 돌려주지 않게** 됐습니다.

S16은 그 강한 GC를 **아무도 기다리지 않는 순간**으로 옮깁니다:

1. 레이드 종료 요청이 끝나고 `DelaySeconds`(30초) 기다림
2. 플레이어 요청이 `QuietSeconds`(3초) 동안 없을 때 실행 — 핑·알림 폴링은 요청으로 안 칩니다
3. `MaxWaitSeconds`(120초)가 지나도 조용해지지 않으면 그냥 실행 (바닐라도 같은 비용을 로딩 화면에서 냈으니 손해 없음)
4. 그 사이 새 레이드가 시작되면 취소, 서버가 쥔 메모리가 `MinCommittedMb`(512MB) 미만이면 생략

**합치면 바닐라와 같은 GC를 레이드당 한 번 하되, 로딩 화면이 아니라 결과 화면에서** 하는 셈입니다.
실행 중 잠깐(보통 수십~수백 ms) 서버 응답이 멈추지만, 그때는 보통 아무 요청도 없습니다.

## 설정 화면 — 런처 모드 페이지 / F12 (2.2부터)

**런처에서:** SPT 런처 → 모드 페이지 → **CompoundingPerf** (주소 `/compoundingperf`, 게임 안 켜도 됨). 아래 표와 같은 설정·서버 상태를 보여 주고 바꾸면 바로 적용됩니다.

**게임에서:** **F12 → `CompoundingPerf.Client`**. 서버 `config.json` 이 기준이라, 게임을 켜면 서버의 현재 값을 읽어 와서 보여 줍니다.
값을 바꾸면 **0.8초 뒤 서버에 바로 적용**되고(슬라이더를 움직이는 동안은 마지막 값 한 번만 보냄) `config.json` 에도 저장됩니다.

| F12 분류 | 내용 |
|---|---|
| 00. 상태 · 언어 | 언어 (한국어 / English) · 서버 연결 여부 · 서버 모드 버전 · 서버 메모리 · 마지막 레이드 후 정리 결과 · 디버그 로그 경로 · 마지막 적용 결과, `서버에서 다시 읽기` 버튼 |
| 01. 레이드 후 서버 정리 (S16) | 사용 / 기다릴 시간 / 조용해야 하는 시간 / 최대 대기 / 최소 서버 메모리 |
| 02. 레이드 시작 GC (S15) | 사용 / 방식 (백그라운드·건너뛰기·바닐라) |
| 03. 플리마켓 강제 GC 제거 (S8) | 사용 |
| 04. 응답 압축 (S9) | 사용 / 압축 수준 (빠름·균형·최소 크기·압축 안 함) |
| 05. 봇 장비 버그 수정 (S12) | 사용 |
| 06. 알림 대기 (S13) | 사용 |
| 07. 저장 건너뛰기 (S11 · 위험) | 사용 / 그래도 저장하는 간격 |
| 08. 디버그 로그 | 사용 / 요약 간격 / 느린 요청 기준 / 보관할 파일 수 |

- 서버 콘솔에도 바뀐 내용이 한 줄로 찍힙니다: `F12 settings applied and saved: Server.FastCompression.Level "Fastest" → "Optimal"`
- `MasterEnabled` 는 F12에 없습니다 — 패치 설치 여부 자체라서 `config.json` 수정 + 서버 재시작으로만 바뀝니다.
- 서버에 연결이 안 되면 상태 칸에 이유가 나오고 10초마다 다시 시도합니다.
- `config.json` 을 직접 고쳐도 되지만, 그때는 **서버를 재시작**해야 반영됩니다(F12로 바꾼 것만 즉시 적용).
- **RAM 클리너(zzap 2.12.0 이상)** 를 같이 쓰면 같은 항목을 브라우저 `http://127.0.0.1:6977/server` 에서도 바꿀 수 있습니다(아래 "RAM 클리너와 같이 / 따로 쓸 때").

## 디버그 로그 (확인용)

F12 → `08. 디버그 로그` → `사용` 을 켜면 **바로** 켜집니다(재시작 불필요). `config.json` 의 `"Debug": {{ "Enabled": true }}` 로 켜도 됩니다(재시작 필요).
파일: `E:\SPT 4.1\SPT_Runtime\user\logs\CompoundingPerf\CompoundingPerf-debug-날짜-시간.log`
(서버 켤 때마다 새 파일, 최근 `KeepFiles`(10)개만 보관). 서버 콘솔에도 시작할 때 경로가 한 줄 나옵니다.

| 줄 머리 | 언제 | 내용 |
|---|---|---|
| `[selfcheck]` | 서버 시작 | 기능마다 **패치가 걸렸는지**(`ok`/`inactive`)와 **설정이 켜져 있는지** |
| `[raid]` | 레이드 시작/끝 | 레이드 시작 응답에 걸린 시간, 레이드 동안 서버 메모리·GC·할당량 변화 |
| `[S15]` | 레이드 시작 | 시작 GC가 응답을 몇 ms 붙잡았는지 (`Mode: Vanilla` 로 바꿔서 비교 가능) |
| `[S16]` | 레이드 후 | 정리를 했는지/왜 건너뛰었는지, 전후 메모리, 멈춘 시간 |
| `[S8]` | 플리 매물 만료 때 | 강제 GC를 건너뛴 기록 |
| `[bots]` | 봇 생성 요청마다 | 몇 마리를 몇 ms 걸려 만들었는지 + 레이드별 합계 (봇 모드가 무거우면 마리당 시간이 큼) |
| `[slow]` | 느린 요청 | `SlowRequestMs`(250ms) 이상 걸린 요청 하나하나 |
| `[requests]` | 레이드 시작/끝 | 그 구간에서 서버 시간을 가장 많이 쓴 주소 상위 12개 |
| `[summary]` | `SummaryIntervalMinutes`(5분)마다 | 서버 메모리, GC 횟수·멈춘 시간, 스레드, 기능별 작동 횟수 |

부담: 켜 있는 동안 요청 하나당 시간 측정 한 번 정도입니다. 확인이 끝나면 다시 `false` 로 꺼 두면 됩니다.

---

## 4.1이 가져간 것 — 기능 5개 폐기

4.0에서 돌던 11개 중 6개가 없어졌고, 그중 5개는 **SPT 4.1이 같은 일을 직접 하기 때문**
입니다. 추측이 아니라 4.1.5 서버 어셈블리를 직접 읽고 확인했습니다.

| 폐기 | 4.1이 하는 일 |
|---|---|
| **S1 ProfileSaveDebouncer** | `SaveProfileAsync` 에 프로필별 `SemaphoreSlim` 이 생겨서 같은 프로필 저장이 겹치지 않습니다. 코얼레서가 합칠 게 없어졌습니다 |
| **S2 ResponseCache** | items / globals / handbook / customization / hideout 같은 무거운 엔드포인트가 `StreamedJsonBody` 를 반환해 응답 스트림으로 직행합니다. **캐싱할 문자열이 애초에 안 만들어집니다** — 우리가 하던 것보다 나은 해법 |
| **S6 ThreadSafeRandom** | `RandomUtil` 에 공유 `System.Random` 자체가 없습니다. `RandomNumberGenerator` 를 쓰는데 이건 원래 스레드 세이프 |
| **S7 ResponseSanitizer** | `ClearString` 이 이미 `SearchValues` 단일 스캔 + 풀 버퍼입니다. 우리가 넣으려던 그 최적화가 이미 들어가 있음 |
| **S10 ThreadSafeCaches** | `ItemBaseClassService` 에 `Lock` 이 생겼고, `HandbookHelper` 의 지연 초기화는 무해한 참조 대입이고, **SPT 내부에 `ItemFilterService` 블랙리스트 변경 호출자가 하나도 없습니다.** 남은 레이스는 "모드가 레이드 중에 다른 스레드에서 블랙리스트를 쓰는" 경우뿐인데, 그걸 막으려면 루팅이 계속 때리는 `IsItemBlacklisted` 를 패치해야 합니다. 비용이 이득에 비해 안 맞아서 뺐습니다 |

여섯 번째는 **S13의 웹소켓 절반**입니다. 4.1은 메시지를 `byte[]` 로 **한 번만** 직렬화해서
`SendRawToSocketsAsync` 에 넘기고, 거기서 `_sendGates` 의 **소켓별 `SemaphoreSlim`** 을 잡으며
전역 락은 목록 스냅샷 뜨는 동안만 잡습니다. 정확히 우리가 넣으려던 것입니다. 롱폴 절반만
살아남았습니다.

그 이전에 정직한 테스트를 통과 못 해서 빠진 것들: 배경 루트 사전생성, 셰이더 프리웜,
레이드 후 GC — 그리고 1.3에서 로그 필터링(S3/C4)과 라우트 디스패치 메모이제이션(S14).
S14는 FIKA에서 런처 응답이 바뀌는데 원인을 끝까지 설명하지 못해서 뺐습니다. **동작이
같다는 걸 증명 못 하는 성능 기능은 출시 안 합니다.**

### 넣을까 하다가 안 넣은 것

`RagfairOfferGenerator.GenerateDynamicOffers` 가 assort 하나당 `Task.Factory.StartNew` 를
**수천 개** 만들고 `Task.WaitAll` 로 블록합니다. 파티셔닝된 `Parallel.ForEach` 면 할당도
로드밸런싱도 훨씬 낫습니다. 안 넣은 이유: **메서드 본문을 통째로 Harmony prefix로
갈아끼워야 하는데**, 그건 나머지 기능들이 일부러 피하는 형태고, 여기서 이득을 측정할 방법이
없었습니다. 위 규칙을 제 코드에도 똑같이 적용했습니다.

---

## 어떻게 동작하나 — 4.1에서 통째로 바뀐 부분

4.0까지는 모든 기능이 **DI 서브클래싱**이었습니다. `Injectable.TypeOverride` 로
`CoalescingSaveServer : SaveServer`, `CachingHttpRouter : HttpRouter` 같은 걸 등록해서 컨테이너
해석 시점에 내장 클래스를 대체했습니다. 평범한 C# 가상 디스패치라 IL 수술이 없고, **다른
모드가 그 클래스에 건 Harmony 패치도 그대로 살아 있었습니다** — 우리 서브클래스가 곧 그들이
패치한 그 객체였으니까요.

**SPT 4.1은 그 선택지를 없앴습니다.**

- `Injectable` 어트리뷰트에서 `TypeOverride` 속성이 **삭제**됨
- `SaveServer` / `RagfairServer` / `RandomUtil` / `SptWebSocketConnectionHandler` 가 **sealed**
- 이 모드가 오버라이드하던 **메서드 11개 중 virtual인 것이 0개**
- 그중 3개는 **이름조차 없어짐** — HTTP 응답, 웹소켓 송신, 라우터 디스패치 경로가 재작성됨

그래서 살아남은 기능들은 전부 Harmony 패치입니다. **이건 이 모드가 자랑하던 호환성 특성을
잃는다는 뜻이고, 숨길 일이 아니니 여기 적어둡니다.** 대신 가능한 한 좁게 만들었고, 둘은
원작보다 **더** 좁습니다.

- **S8 / S15** — 예전엔 메서드를 재구현했습니다. 지금은 `GC.Collect` **호출 명령 하나**를
  동일 시그니처 메서드 호출로 바꿉니다. 나머지 명령은 컴파일러가 뽑은 그대로 남습니다.
  동작 동일성이 "구현을 잘해서"가 아니라 **구조적으로** 보장되고, 판단이 패치가 아니라
  메서드 안으로 들어가서 스위치를 실행 중에 바꿀 수 있습니다 — 2.2의 F12 화면이 이걸 씁니다
- **S9** — 예전엔 응답 전송 메서드를 통째로 대체했습니다. 지금은 `ZLibStream` 생성자에
  들어가는 `CompressionLevel` **상수 하나만** 바꿉니다
- **S11** — `SaveProfileAsync` 에 스킵 prefix, 라우터에 **요청 경로만 읽는** prefix.
  라우터 쪽을 못 찾으면 **저장 스킵도 설치하지 않습니다** — 표시하는 절반 없이 스킵만
  살면 세션이 영원히 깨끗해 보여서 진짜 저장을 날립니다
- **S12** — 사본을 반환하는 postfix
- **S13** — `Thread.Sleep` 을 `await Task.Delay` 로 바꾸는 prefix. 300ms 간격, 15초 예산,
  기본 알림 폴백까지 전부 동일

모든 패치는 대상을 못 찾으면 **조용히 아무 일도 안 하는 대신 로드 시점에 경고**를 찍습니다.

---

## RAM 클리너와 같이 / 따로 쓸 때

**RAM 클리너(zzap)** 는 **게임** 쪽, 이 모드는 **서버** 쪽입니다. 서로에게 기대지 않아서 **하나만 써도, 둘 다 써도** 그대로 동작합니다.
둘 다 있으면 RAM 클리너가 이 모드의 F12 항목을 찾아서 웹 페이지 탭으로 보여 줄 뿐입니다(이 모드의 동작은 같음).

| 조합 | 동작 |
|---|---|
| 이 모드만 | 그대로 동작. 게임 쪽 메모리·끊김은 손대지 않습니다 |
| RAM 클리너만 | 그대로 동작. 서버 쪽은 바닐라(레이드 시작 때 로딩 화면에서 서버 GC) |
| 둘 다 | 레이드 후 정리가 **게임 20초 → 서버 30초+** 로 나뉘어 겹치지 않습니다(기본값 기준). RAM 클리너 웹 페이지에 **'서버 최적화' 탭**이 생기고, 언어도 RAM 클리너를 따라갑니다 |
| F12 플러그인만 (서버 모드 없음) | 상태 칸에 "서버 모드가 없습니다" 표시, 5분마다만 확인. 다른 동작 없음 |

- 둘 다 쓰면서 RAM 클리너의 `After raid delay` 를 늘렸다면, 이 모드의 `레이드 끝나고 기다릴 시간` 을 그보다 **10초 이상 길게** 두세요.
  둘이 동시에 돌면 오류는 없지만 메뉴에서 잠깐 더 버벅일 수 있습니다.
- 서버가 메모리를 돌려주는 효과는 RAM 클리너 로그의 `[mem after raid]` 줄(레이드 후 15초마다, 3분간)의 "시스템 여유"에서 보입니다.
- 문제 제보 때 둘 다 쓰고 있다면 RAM 클리너 로그(`BepInEx\RamCleaner\`)와 이 모드의 디버그 로그를 **같이** 보내 주세요. 둘 다 PC 시각 기준이라 시간을 맞춰 볼 수 있습니다.

## 호환성

- 모드 없는 **SPT 4.1.5** 기준
- **SAIN·ORBIT와 대조했습니다 (2026-10-04, 문제 없음)**:
  - 두 모드의 서버 쪽(`SAINServerMod`, `Orbit.Server`)은 **Harmony 패치가 없고** 자기 주소(`/sain/...`, `/orbit/...`)만 씁니다 → 이 모드의 패치·`/compoundingperf/...` 주소와 겹치지 않습니다.
  - SAIN 프리셋은 스트리밍 응답이라 S9(빠른 압축)가 적용됩니다 — 같은 PC 안이라 크기 차이는 무의미하고, 게임 쪽은 어떤 압축 수준이든 그대로 풉니다.
  - ORBIT는 레이드 로딩 중(`BotsController.Init`)에 서버 설정을 받아 가고, SAIN은 시작·프리셋 변경 때 받아 갑니다. 레이드 **후**에 서버를 오래 붙잡는 요청은 없어서
    S16의 "조용할 때 정리"와 부딪히지 않습니다(혹시 요청이 오면 S16이 끝날 때까지 기다렸다 실행).
  - SAIN의 프리셋 동기화 웹소켓은 이 모드가 건드리지 않습니다(4.1에서 웹소켓 쪽 기능은 폐기됨).
  - S11(저장 건너뛰기, 기본 꺼짐)을 켜도 SAIN·ORBIT의 저장 요청은 "변경 있음"으로 처리되어 안전한 쪽으로 동작합니다.
- **APBS(봇 장비)·ABPS(봇 배치)와 같이 쓰는 것을 전제로 대조했습니다 (2.2.1)**:
  - APBS는 PMC 장비를 자기 생성기로 만듭니다. S12는 APBS 쪽을 복사하지 않고(읽기만 하므로), 바닐라가 만드는 봇(스캐브·보스 등)에만 적용됩니다.
  - 디버그 로그의 `[bots]` 마리당 시간에는 APBS 장비 생성 시간이 포함됩니다 — 모드 아이템 허용 범위를 넓히면 여기 숫자가 커지는지 보면 됩니다.
  - APBS·ABPS는 `/client/match/local/start`·`/end` 에 자기 처리를 붙입니다. 이 시간은 `[raid] raid-start response built` 가 아니라
    `[requests]` 표의 해당 주소 합계에 들어갑니다.
- **FIKA는 4.1에서 미검증입니다.** FIKA를 특별히 겨냥한 코드는 없고 4.0 계열은 FIKA 2.3.x
  에서 테스트됐지만, 이 버전은 안 해봤습니다
- 다른 모드와 공존하도록 설계돼 있지만 **4.1에서는 그 보장이 4.0 때보다 약합니다.**
  5개 중 4개가 Harmony 패치이고, 그중 바닐라 호출을 막을 수 있는 건 S11의 저장 스킵
  하나뿐입니다. 같은 메서드를 패치하는 모드가 있으면 DI 서브클래스 시절과 달리 **순서가
  영향을 줍니다**

## 설치

릴리스 zip을 **`E:\SPT 4.1` 에 그대로 풀면** 됩니다. 들어 있는 것:

| 파일 | 위치 | 역할 |
|---|---|---|
| `CompoundingPerf.dll` + `config.json` | `SPT_Runtime\user\mods\CompoundingPerf\` | 서버 모드 (최적화 본체) |
| `CompoundingPerf.Client.dll` | `BepInEx\plugins\CompoundingPerf.Client\` | F12 설정 화면 (없어도 서버 모드는 동작, config.json으로 설정) |

2.1 이하에서 올라가는 경우 `config.json` 을 덮어써도 됩니다(설정은 F12에서 다시 맞추면 됨).
`MasterEnabled: false` 면 패치를 아예 설치하지 않습니다 (A/B 비교용, config.json + 서버 재시작).

## 빌드

평소엔 이거면 됩니다 — 실제로 배포되는 것만 빌드합니다:

```
dotnet build CompoundingPerf.csproj -c Release
```

`$(SptRoot)\SPT_Runtime\user\mods\CompoundingPerf\` 로 dll + config.json 을 바로 복사하고,
`SptRoot` 에 게임 어셈블리가 있으면 클라이언트 플러그인도 빌드해서 `BepInEx\plugins\CompoundingPerf.Client\` 에 복사한 뒤
둘을 합친 zip을 `release\` 에 만듭니다(게임 어셈블리가 없으면 서버만 든 zip + 경고).
기본 `SptRoot` 는 `E:\SPT 4.1`, `-p:SptRoot=...` 로 덮어쓰기, `-p:SkipDeploy=true` 로 복사 생략
(서버가 켜져 있어서 파일이 잠겨 있을 때).

솔루션은 3개 프로젝트를 묶어둔 편의용입니다:

```
dotnet build CompoundingPerf.sln -c Release
dotnet test  CompoundingPerf.sln -c Release
```

> ⚠️ 솔루션 빌드는 **클라이언트 프로젝트까지 빌드**하므로 `SptRoot` 에 EFT 어셈블리
> (`Assembly-CSharp.dll`, `BepInEx.dll`)가 실제로 있어야 합니다. 없으면 클라 프로젝트에서
> 에러가 납니다 — 서버 dll 자체는 그래도 정상적으로 나오지만, 그럴 바엔 csproj로 빌드하는
> 게 깔끔합니다.

### `client/` 폴더

2.2부터 **F12 설정 화면 플러그인**입니다(`ServerSettingsMenu.cs`, `ServerLink.cs`). 게임 패치는 없고,
서버의 `/compoundingperf/config/get`·`/set` 주소와 평문 JSON(`requestcompressed: 0` / `responsecompressed: 0`)으로만 통신합니다.
백엔드 주소는 게임 실행 인자 `-config={{"BackendUrl":...}}` 에서 읽습니다(`spt-common` 불필요).
예전 벤치마크용 프레임 기록기는 그대로 `#if BENCH` 안에 있어서 `-p:Bench=true` 로 빌드할 때만 들어갑니다.

서버 프로젝트는 `net10.0` + `SPTushonka.Server.Core` 4.1.5 (4.1에서 패키지 ID가
`SPTarkov.*` → `SPTushonka.*` 로 바뀌었고, 안의 네임스페이스는 그대로입니다).
클라이언트는 `netstandard2.1`.

`Lib.Harmony` 는 **2.4.2** 로 고정돼 있습니다. 2.3.3이 아닙니다 — 4.1 서버가 .NET 10에서
도는데 거기서 `System.Reflection.Emit.LocalBuilder` 가 abstract가 됐고, 2.3.3은 패치를 만들다
지역 변수를 선언하는 순간 `MemberAccessException` 을 던집니다.

---

## 확인한 것 / 확인 못 한 것

Harmony 패치는 대상이 옮겨져도 **컴파일이 깨지지 않습니다.** 로드 시점에 실패하거나, 더
나쁘게는 조용히 아무 일도 안 합니다. 그래서 실제 4.1.5 어셈블리와 이 모드를 한 프로세스에
올려서 6개 기능을 각자의 `Apply` 로 **실제로 설치**하고 Harmony가 바인딩했는지 확인했습니다:

```
ok    S8  RagfairServer.ProcessExpiredFleaOffers resolved
ok    S9  AsyncMoveNext(SendZlibJsonAsync) resolved
ok    S9  AsyncMoveNext(SendStreamedJsonAsync) resolved
ok    S11 SaveServer.SaveProfileAsync resolved
ok    S11 HttpRouter.GetResponseObjectAsync resolved
ok    S12 BotHelper.GetBotRandomizationDetails resolved
ok    S13 NotifierController.NotifyAsync resolved
ok    S15 AsyncMoveNext(StartLocalRaidAsync) resolved
ok    S8  transpiler rewrote the GC.Collect call (count=1)
ok    S9  transpiler rewrote both ZLibStream levels (count=2)
ok    S15 transpiler rewrote the raid-start GC.Collect (count=1)
ok    ... 타겟 8개 전부 우리 패치를 달고 있음
ALL PATCHES BIND
```

| | 상태 |
|---|---|
| 패치 대상 8개가 4.1.5에 존재 | **확인** |
| 시그니처 · 주입 파라미터 이름 · `__result` 타입 | **확인** — Harmony가 패치 시점에 셋 다 검증합니다 |
| 트랜스파일러가 실제로 재작성했는지 | **확인** — 예상 개수와 정확히 일치 (1 / 2 / 1). IL을 그냥 흘려보낸 게 아님 |
| 폐기한 5개가 정말 4.1에 들어갔는지 | **확인** — 위 표의 근거는 전부 4.1.5 어셈블리에서 읽은 것 |
| 2.1 추가 패치 4개(`SptHttpListener.HandleAsync`, `StartLocalRaidAsync`, `EndLocalRaidAsync`, `BotController.Generate`) | **확인** — 2.1에서 실제 4.1.5 서버 DLL에 12개 패치를 전부 걸고, 메서드를 직접 호출해 디버그 로그·S16 정리가 실제로 나오는 것까지 확인 |
| 유닛 테스트 | **41개 통과** |
| F12 ↔ 서버 (2.2) | **확인** — 실제 클라 플러그인 dll을 Mono(게임과 같은 런타임)에서 돌리고, 실제 서버 설정 라우트 코드를 띄운 HTTP 서버와 통신시켜 확인: 서버 값 읽어 오기, 두 번 바꾼 걸 한 번에 전송, 즉시 적용, config.json 저장(설명 주석 유지), 디버그 로그 즉시 켜짐, 범위 밖 값 보정 |
| 실서버 구동 | **안 함** — 디버그 로그로 확인 예정 |
| 인게임 성능 측정 | **안 함** — 위 성능 서술은 4.1.5 소스에서 읽은 것이고, CHANGELOG의 수치는 **4.0 기준**입니다 |

## 안 되면 여기부터 보세요

- **기능이 안 먹는다**: 서버 로그에 `[CompoundingPerf/Sxx] ... not found` 경고가 있는지
  보세요. 4.1.5보다 새 빌드면 대상 메서드가 또 움직였을 수 있습니다
- **`MemberAccessException` 이 뜬다**: 서버가 들고 있는 `0Harmony.dll` 이 구버전입니다
  (위 .NET 10 이슈)
- **프로필이 저장이 안 되는 것 같다**: `SaveDirtyTracking` 을 켜뒀는지 확인하세요.
  기본값은 꺼짐이고, 이게 유일하게 저장을 건너뛸 수 있는 기능입니다
- **레이드 로딩은 빨라졌는데 메모리가 계속 는다**: 2.1부터는 S16이 레이드 후에 정리합니다.
  서버 콘솔에 `post-raid cleanup` 줄이 나오는지 보고, 안 나오면 디버그 로그의 `[S16]` 줄에
  건너뛴 이유가 있습니다. 그래도 늘면 `RaidStartGc.Mode` 를 `Vanilla` 로 되돌려 보세요
- **뭔가 이상하다**: 디버그 로그를 켜고 레이드 한 판 돈 뒤 그 파일을 보내 주세요

## License

MIT — 원작자 EchoStarz, `LICENSE` 참고.
