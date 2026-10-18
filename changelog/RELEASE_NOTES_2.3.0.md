# CompoundingPerf-zzap--Bootleg- v2.3.0

**태그:** `v2.3.0` (master `304e091`)

**원작:**

1. **EchoStarz** — CompoundingPerf
   - Link: https://forge.sp-tarkov.com/mod/2415/compoundingperf
   - License: MIT
2. **TominoCZ** — Smoothie (C1·C2 게임 쪽 수정의 아이디어만 참고, 코드는 새로 작성)
   - Repository: [TominoCZ/Smoothie](https://github.com/TominoCZ/Smoothie)
   - License: CC BY-NC-SA 4.0

**이전 릴리즈:** 2.2.2 (GitHub 릴리즈 `v1`, 2026-10-05, 그 뒤 커밋 4개) → **이번: v2.3.0** (기능 추가)

**<변경점>**

**1. 게임 쪽 CPU 낭비 수정 2개 — F12 `09. 게임 쪽 CPU 낭비 고치기 (C1·C2)` (새 분류, 둘 다 기본 켜짐)**
- 이 모드는 원래 서버 쪽 최적화 모드인데, 이번에 게임 안에서 도는 작은 수정 2개가 F12 플러그인에 들어갔습니다. 이 두 항목은 서버로 보내지 않고 게임에서만 동작합니다(런처 모드 페이지에는 안 보임).
- **C1 레이드 중 메뉴 배경 영상 멈추기**
  - 문제: 메인 메뉴 배경 중 하나는 영상 4개(2048x1024, 60fps)를 계속 돌립니다. 레이드에 들어가면 게임이 배경만 숨기고 영상은 그대로 둬서, 레이드 내내 보이지 않는 영상을 디코딩하느라 CPU를 썼습니다.
  - 이제: 2초마다 메뉴 배경이 숨겨졌는지 보고, 숨겨져 있으면 재생 중인 영상을 일시정지, 메뉴로 돌아오면 멈춘 것만 다시 재생합니다. 게임 함수를 고치지 않는 방식입니다(숨긴 직후 최대 2초는 영상이 더 돌 수 있음).
- **C2 착지한 에어드랍 상자 물리 끄기**
  - 문제: 에어드랍 상자는 떨어지기 시작할 때 게임의 물리 관리 목록에 들어가고 다시 빠지지 않습니다. 목록에 하나라도 있으면 게임은 맵 전체 물리를 매 프레임 계산해서, 에어드랍 하나가 레이드 끝까지 물리 계산을 돌게 했습니다(맵·봇 수에 따라 프레임당 1~3ms).
  - 이제: 상자가 완전히 내려앉은(기울어짐까지 끝난) 뒤 목록에서만 뺍니다. 상자 위에 올라서기·루팅은 그대로입니다. 상자를 시뮬레이션하는 쪽(솔로, Fika 호스트)에서만 해당되고, 레이드 중에 켜면 그 뒤에 떨어지는 상자부터 적용됩니다.
- 이 게임 버전과 안 맞으면 그 항목만 꺼지고 로그에 `[C2 ...] skipped` 같은 줄이 남습니다.
- 확인 상태: 대상 게임 코드와 대조, 빌드만 확인. 게임 미검증.

**2. RAM 클리너(zzap) 웹 '서버 최적화' 탭**
- RAM 클리너 2.15.0의 이 탭에도 09번 두 항목이 보이고 거기서 바꿀 수 있습니다. 안내 문구가 "09번은 게임에서만"으로 바뀌었습니다.

**3. 기타**
- README 맨 위 원작자 표기를 원작마다 번호를 붙여 나눴습니다.
- 버전: 서버 모드·F12 플러그인·zip 이름(`CompoundingPerf-2.3.0.zip`) 2.3.0. 서버 쪽 동작은 2.2.2와 같습니다.

**알려진 점**
- C1·C2는 게임 미검증입니다. 이상하면 F12 `09.`에서 그 항목만 끄면 원래대로 돌아갑니다.
- zip에는 서버의 `config.json`도 들어 있습니다. 덮어쓰면 서버 설정이 기본값으로 돌아가니, 설정을 바꿔 쓰셨다면 `config.json`은 빼고 덮어쓰거나 F12/런처 모드 페이지에서 다시 맞추세요. (09번 두 항목은 게임 쪽 설정이라 영향 없음)
- 나머지는 2.2.2 릴리즈 노트와 같습니다.

**설치 / 업데이트**
- `CompoundingPerf-2.3.0.zip` 압축을 풀어 SPT 폴더(예: `E:\SPT 4.1`)에 덮어쓰기. 두 곳에 들어갑니다: `SPT_Runtime\user\mods\CompoundingPerf\`(서버 모드), `BepInEx\plugins\CompoundingPerf.Client\`(F12 설정 + 게임 쪽 수정).
- 배포 zip은 제작자 PC에서 실제 게임 파일로 빌드한 것입니다.

------------------------------------------------------------------------------------------------------------------------------------------------------

**<Changes (English)>**

**1. Two game-side CPU fixes — F12 `09. Game-side CPU waste fixes (C1·C2)` (new, both on by default)**
- This is a server-side mod; these two small fixes run in the game through the F12 plugin and are never sent to the server (not on the launcher mod page).
- **C1 Pause the menu background video in raid:** one menu background keeps four 2048x1024 60 fps videos running; going into a raid the game hides only the background, so they decoded all raid long. A check every 2 s now pauses them while the background is hidden and plays exactly those again back in the menu. No game code is patched (up to 2 s of extra playback right after hiding).
- **C2 Release a landed airdrop's physics:** a crate is added to the game's physics list when it starts falling and never removed; while anything is on the list the whole map's physics runs every frame (1–3 ms a frame depending on map and bots). The crate is now taken off the list once it has fully landed. Standing on it and looting work as before. Applies where the crate is simulated (solo, Fika host); switched on mid-raid it applies to crates that land afterwards.
- A fix whose target is missing in this game build is skipped and logged.
- Status: targets checked against the game code, built only. Untested in game.

**2. RAM Cleaner (zzap) "Server optimisation" web tab**
- RAM Cleaner 2.15.0 shows and changes the two 09 items too; its notice says they stay in the game.

**3. Other**
- README credits numbered per original work.
- Version 2.3.0 (server mod, F12 plugin, zip name `CompoundingPerf-2.3.0.zip`). Server behaviour is the same as 2.2.2.

**Known issues**
- C1/C2 are untested in game; switch either off in F12 `09.` to get the old behaviour back.
- The zip contains the server `config.json`; overwriting it resets the server settings to defaults. Skip that file or set them again in F12 / the launcher mod page.

**Install / update**
- Extract `CompoundingPerf-2.3.0.zip` into your SPT folder and overwrite (server mod + F12 plugin).

------------------------------------------------------------------------------------------------------------------------------------------------------

**!!!!!!!!!!!PLEASE READ THIS!!!!!!!!!!!**

Hi everyone,

I'd like to share a few thoughts and important warnings regarding the process of porting mods from version 4.0 to 4.1.

Following the recent "ECOT" mod incident, I've had a lot of time to reflect on my actions.
I’m not here to lecture anyone—I’m writing this simply in the hope that you’ll keep these points in mind.


**1. I am not a professional programmer; I am mostly a "vibe coder" who relies heavily on AI.**

By that, I mean I’m not someone who spends days designing and painstakingly crafting mods by hand from scratch.
At the same time, having tinkered with simple mod edits back in the TS/JS (SPT 2.3.1) days, I know firsthand how much time, effort, and care goes into this process.
Therefore, even if a mod is covered under an MIT License, if the original author requests that I delete the source code and stop distribution, I will willingly comply.


**2. Please DO NOT re-distribute or re-upload these modified mods to 3rd/4th party places.**

Even if my ported version has some fixes or improvements, please do not treat it as a long-term solution. Unlike official releases on "THE FORGE," I do not perform thorough debugging or full security scans.
Furthermore, imagine how disruptive it would be if someone mistook my ported build for an "official release" and pestered the original author about bugs in my version. It would be terrible.
Moving forward, I intend to approach any porting work with the utmost respect for original modders.
If I had spent days pouring my heart into making a mod, only to have someone take my code and modify it however they pleased, I wouldn't feel good about it either.
Therefore, when writing README.md files on GitHub, I will always credit the original creators, and if it's not a mod I built from scratch, I will fully respect the original modder's decisions and opinions.


**3. Please respect the original creators.**

If people ignore warning 2. and act as if my port is the "real deal," I would feel immense guilt.
Please do not do that. As the saying goes, "When in Rome, do as the Romans do"—and showing proper respect and courtesy is essential.
Instead, please visit the original creators' comment sections on The Forge to leave warm words of appreciation, encouragement, or even buy them a coffee/leave a tip if you can


**Thank you for reading this long post.**
**Stay safe and stay happy!**

**^^7**

------------------------------------------------------------------------------------------------------------------------------------------------------

**!!!!!!!!!!!꼭 읽어주세요!!!!!!!!!!**

안녕하세요 여러분

4.0 -> 4.1로 포팅하는 과정에 대해 드릴 말씀과 주의사항을 전달하고자 합니다.

이번 "ECOT" 모드 사건 이후로, 많은 반성의 계기를 가지게 되었습니다

뭔가, 설교를 드리는게 아니라 참고 해주셨으면 하는 마음에 글을 작성합니다.


**1. 저는 전문 프로그래머가 아니고, AI를 주로 이용하는 바이브코더입니다.**

해당말의 뜻은, 저는 매일 구상하고 힘들게 한땀한땀 손으로 모드를 만드는 사람이 아니고

또한, TS/JS (SPT 2.3.1) 시절에 간단하게 모드 수정을 거치며 이 과정이 얼마나 노력을 들이고, 시간이 필요한지 잘 알고 있습니다.

그렇기에, 해당 모드를 수정하면서 아무리 MIT 라이선스를 받았더라도 원 모드 제작자가 소스코드 삭제 및 배포를 중단해달라고 하면 중단할 의향이있습니다.


**2. 무단으로 수정한 모드를 "3차 4차 배포하지 마십쇼"**

제가 만든 버전이 개선점이 있어도, 오래 사용하지 마세요 저는 "FORGE" 에 올린 것 처럼 바이러스 검사나 디버깅을 완벽하게 수행하지 않습니다.

또한, 제가 포팅한 버전을 "진짜 릴리즈된 버전" 이라고 생각하고, 제 포팅 모드를 들고 원작자에게 가면 얼마나 민폐일까요? 끔찍합니다

저는 모더들을 최대한 존중하는 방향으로 포팅할 계획입니다.

아마, 제가 모드를 제작할 때 하루를 갈아넣어 만들었는데 누군가가 멋대로 코드를 뺏겨 지 멋대로 작성했다면 기분을 썩 좋지않을 것 같습니다.

고로 GITHUB에 README.MD 작성 시, 원작자 표기 및 제가 실제로 제작하지 않은 모드라면 최대한 해당 모드를 제작한 모더의 의견을 존중하는 방향으로 나아갈 것입니다.

**3. 원작자를 존중해주세요**

만일 2번 주의사항을 어겼을 시, 제가 포팅한 모드가 "진짜 모드다!" 이런 느낌이면 진짜 죄책감에 휩싸일 것 같습니다.

그런 짓은 하지말아주고, 로마에 왔으면 로마법을 지키듯이 한국에서는 예의를 갖추는 것이 미덕입니다.

해당 모드 제작자의 FORGE COMMENT 창에 들어가 따뜻한 위로와 감사하다는 격려 또는 그들에게 TIP을 건네주세요


**긴 글 읽어주셔서 감사합니다.** 

**행복하세요! ^^7**
