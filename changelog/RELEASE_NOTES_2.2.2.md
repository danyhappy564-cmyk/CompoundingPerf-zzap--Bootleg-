# CompoundingPerf (zzap) 2.2.2 릴리즈 노트 (2.0.0 대비)

> 원작: EchoStarz — CompoundingPerf (MIT). 이 버전은 비공식 SPT 4.1 포팅입니다. 문제가 생겨도 원작자에게 문의하지 말아 주세요.
> **SPT 서버 쪽** 최적화 모드입니다. 레이드 로딩, 메뉴 반응, 서버가 쓰는 메모리를 다룹니다. 레이드 중 FPS는 거의 바뀌지 않습니다(그건 게임 쪽 몫 — RAM 클리너).

**<설치>**

- zip을 **SPT 설치 폴더(예: `E:\SPT 4.1`)에 그대로 풀면** 됩니다. 두 곳에 들어갑니다:
  - `SPT_Runtime\user\mods\CompoundingPerf\` — 서버 모드 (최적화 본체)
  - `BepInEx\plugins\CompoundingPerf.Client\` — F12 설정 화면 (없어도 서버 모드는 동작)
- **2.0.0에서 올라오는 경우:** 덮어쓰면 됩니다. `config.json`도 덮어써도 되고, 설정은 F12에서 다시 맞추면 됩니다.

---

**<변경점>**

- **F12 설정 화면 (새로 생김)**

1. 게임에서 **F12 → `CompoundingPerf.Client`** 에서 모든 기능을 한글로 보고 바꿀 수 있습니다. 항목에 마우스를 올리면 설명이 나옵니다.
2. 값을 바꾸면 **0.8초 뒤 서버에 바로 적용**되고, 서버의 `config.json`에도 저장돼서 다음에 서버를 켜도 유지됩니다. 서버 재시작이 필요 없습니다.
3. 맨 위 `00. 상태` 칸에 서버 연결 여부, 서버 메모리, 마지막 레이드 후 정리 결과가 나옵니다.

- **레이드 후 서버 정리 (새 기능 S16, 기본 켜짐)**

1. 레이드가 끝나고 서버가 한가해지면(결과 화면을 보는 동안) **서버 메모리를 정리해 윈도우에 돌려줍니다.**
2. 바닐라는 같은 정리를 레이드 **시작** 때 로딩 화면에서 해서 로딩이 길어졌습니다. 이 모드는 그걸 결과 화면 시간으로 옮긴 것입니다(로딩은 빠르게, 메모리는 돌려받게).
3. 할 때마다 서버 콘솔에 `post-raid cleanup: … → … MB` 한 줄이 나옵니다.

- **봇 장비 버그 수정(S12) 가볍게**

1. 야간 레이드 장비 보정이 공용 설정에 쌓이던 바닐라 버그를 막는 기능인데, 필요 없는 곳까지 설정을 복사하고 있었습니다(봇 1명당 수십 번). 이제 필요한 곳에서만 복사합니다.
2. **APBS를 쓰면** APBS가 만드는 봇은 복사가 아예 없습니다.

- **디버그 로그 (새 기능, 기본 꺼짐)**

1. F12 → `08. 디버그 로그` 를 켜면 바로 `SPT_Runtime\user\logs\CompoundingPerf\` 에 확인용 로그가 생깁니다. 기능이 제대로 걸렸는지, 레이드마다 서버 메모리·느린 요청·봇 생성 시간이 남습니다.
2. 문제를 제보할 때 이 파일이 있으면 원인 찾기가 훨씬 쉽습니다. 확인이 끝나면 꺼 두세요.

- **고친 것**

1. 2.0의 `Telemetry` 설정은 켜도 아무 일도 하지 않았습니다 → 디버그 로그로 바꾸고 설정에서 뺐습니다(예전 config에 남아 있어도 무시됨).
2. 설명에 적힌 설치 경로(`SPT\user\mods` → `SPT_Runtime\user\mods`)와 "설정이 재시작 없이 바로 적용된다"는 틀린 설명을 바로잡았습니다(이제는 F12로 바꾸면 실제로 바로 적용).

- **다른 모드와 같이 쓸 때 (코드로 대조함)**

1. **RAM 클리너(zzap)** 와 같이 써도 되고 따로 써도 됩니다. 서로를 찾지 않고, 레이드 후 정리 시점도 게임 20초 / 서버 30초 이후로 나뉘어 겹치지 않습니다.
2. **APBS · ABPS · SAIN · ORBIT** 과 겹치는 부분이 없는 것을 확인했습니다.
3. F12 플러그인만 깔고 서버 모드가 없으면 상태 칸에 "서버 모드가 없습니다"라고 나오고, 5분마다만 다시 확인합니다.

- **알려진 문제 / 주의**

1. 이번 버전은 실제 서버를 오래 돌려 본 테스트를 아직 거치지 않았습니다(코드 대조와 테스트 환경 확인까지). 이상하면 F12에서 해당 기능(S16, S15 순서로)을 끄면 바로 적용됩니다.
2. FIKA 환경은 확인하지 않았습니다.
3. 모든 기능을 한 번에 끄는 `MasterEnabled` 는 F12에 없습니다. `config.json`에서 바꾸고 서버를 재시작해야 합니다.

- **제보 방법**

1. F12 → `08. 디버그 로그` 를 켜고 레이드를 한 판 돈 뒤, `SPT_Runtime\user\logs\CompoundingPerf\` 의 최신 파일을 보내 주세요.
2. RAM 클리너도 같이 쓰고 있다면 `BepInEx\RamCleaner\` 의 최신 로그도 같이 보내 주세요(둘 다 PC 시각 기준이라 맞춰 볼 수 있습니다).

^^7

------------------------------------------------------------------------------------------------------------------------------------------------------

# CompoundingPerf (zzap) 2.2.2 release notes (vs 2.0.0)

> Original: EchoStarz — CompoundingPerf (MIT). This is an unofficial SPT 4.1 port; please don't contact the original author about it.
> A **server-side** optimisation mod: raid loading, menu responsiveness and the server's memory. In-raid FPS barely changes (that's the game side — RAM Cleaner).

**Install**

- Extract the zip **straight into your SPT folder (e.g. `E:\SPT 4.1`)**. It goes to two places:
  - `SPT_Runtime\user\mods\CompoundingPerf\` — the server mod (the optimisations)
  - `BepInEx\plugins\CompoundingPerf.Client\` — the F12 settings page (the server mod works without it)
- **Coming from 2.0.0:** just overwrite. Overwriting `config.json` is fine too; set things again in F12.

---

**Changes**

- **F12 settings page (new)**

1. In game, **F12 → `CompoundingPerf.Client`** shows every feature with Korean names; hover an entry for its description.
2. A change is **applied on the server 0.8 s later**, and saved into the server's `config.json` so it stays after a server restart. No restart needed.
3. The `00. 상태` (status) block at the top shows the server connection, server memory and the last post-raid cleanup result.

- **Post-raid server cleanup (new feature S16, on by default)**

1. After a raid, once the server is idle (while you read the results screens), it **compacts the server's memory and hands it back to Windows.**
2. Vanilla does the same cleanup at raid **start**, on the loading screen, which makes loading longer. This moves it to the results screen instead (fast loading, memory still returned).
3. Each cleanup prints one `post-raid cleanup: … → … MB` line in the server console.

- **Lighter bot gear bug fix (S12)**

1. S12 stops a vanilla bug where night-raid gear modifiers pile up in the shared config, but it was copying that config in places that never needed it (dozens of times per bot). It now copies only where needed.
2. **With APBS**, bots made by APBS need no copy at all.

- **Debug log (new, off by default)**

1. Turn on F12 → `08. 디버그 로그` and a log appears right away in `SPT_Runtime\user\logs\CompoundingPerf\`: whether every feature hooked in, and per raid the server memory, slow requests and bot generation times.
2. It makes bug reports much easier to act on. Switch it off when you're done.

- **Fixes**

1. 2.0's `Telemetry` settings did nothing even when turned on → replaced by the debug log and removed (old keys in your config are ignored).
2. Corrected the install path in the docs (`SPT\user\mods` → `SPT_Runtime\user\mods`) and the wrong claim that settings applied without a restart (with the F12 page they now really do).

- **With other mods (checked against their code)**

1. Works with or without **RAM Cleaner (zzap)**. Neither looks for the other, and their post-raid cleanups are split (game at 20 s, server at 30 s+) so they don't overlap.
2. Checked for overlaps with **APBS · ABPS · SAIN · ORBIT** — none.
3. With only the F12 plugin installed and no server mod, the status block says the server mod is missing and it re-checks only every 5 minutes.

- **Known issues / notes**

1. This release hasn't had a long run on a real server yet (code review and test-environment checks only). If anything seems off, switch features off in F12 (S16 first, then S15) — it applies immediately.
2. Not tested with FIKA.
3. `MasterEnabled` (turn everything off at once) is not in F12; change it in `config.json` and restart the server.

- **How to report**

1. Turn on F12 → `08. 디버그 로그`, play one raid, and send the newest file in `SPT_Runtime\user\logs\CompoundingPerf\`.
2. If you also use RAM Cleaner, send the newest log in `BepInEx\RamCleaner\` too (both use your PC's clock, so they line up).

^^7
