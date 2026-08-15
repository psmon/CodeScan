# Graph Reconciliation (v2)

> 그래프를 "매 스캔 새로 만드는 스냅샷"에서 "프로젝트 단위로 증분 재조정되는
> 지식"으로 바꾼 설계. 리셋이 아니라 정보 업데이트 시 관계를 재정렬한다.

## 왜 바꿨나 (v1의 문제)

v1 그래프는 `graph_nodes UNIQUE(scan_id, stable_key)` 로 **scan_id에 종속**됐다.
`InsertScan` 은 매번 새 scan_id로 그래프를 통째 재생성하고 조회는 `MAX(scan_id)`
(최신 세대)만 봤다. 결과:

- 이전 세대에 붙은 큐레이션(`curated=1`, `weight++`)이 최신 세대로 **이월되지 않아
  업데이트 = 사실상 리셋**.
- 죽은 세대가 삭제 없이 쌓여 **DB 무한 증식**.
- diff 없이 전량 재기록.

## v2 모델

식별자를 `(project_id, stable_key)` 로 바꿔 노드/엣지가 스캔을 가로질러 **동일
정체성**을 유지한다. stable_key 는 스캔 불변이어야 한다:

| 종류 | v2 stable_key | 비고 |
|------|---------------|------|
| project | `project:{id}` | |
| file/dir | `path:{relativePath}` | |
| class | `class:{relativePath}:{ClassName}` | |
| method | `method:{relativePath}:{ClassName}:{MethodName}` | fileId·StartLine 제거 → 편집에도 안정. 오버로드는 Phase 1에서 1개로 합쳐짐 |
| comment | `comment:{relativePath}:{StartLine}` | 라인 이동엔 취약(허용) |
| heading | `heading:{relativePath}:{Text}` | |
| author/dependency/doc/doc-meta | 기존과 동일(이미 안정) | |
| curated | `curated:{kind}:{label}` | 큐레이션 전용 |

### 생명주기 컬럼

`graph_nodes` / `graph_edges` 공통:

- `state` — `'active'`(라이브) / `'stale'`(은퇴). 조회는 기본 active만 본다.
- `first_seen_scan` / `last_seen_scan` — 관측 이력(재조정 기준).
- `curated` — 1이면 사람/LLM 지식, 자동 재조정에서 **면제**.
- `weight`(엣지) — **확증한 스캔 수**(Phase 2). 관측한 스캔마다 +1(스캔당 1회,
  같은 스캔 내 반복은 중복 집계 안 함), 상한 999. 미관측 스캔마다 -1로 **감쇠**,
  0에서 은퇴. 조회는 `curated DESC, weight DESC` 로 **재정렬**(강한 관계 우선).

## 재조정 알고리즘 (3-way merge)

`InsertScan(projectId, entries, fullRebuild)` 은 관측분을 stable_key 로 upsert
(id 보존, `last_seen_scan` 갱신, `state='active'`, curated·first_seen 보존)한 뒤
`FinalizeReconcile` 로 마무리한다.

| 상태 | 판정 | 증분(기본) | full(`--full`) |
|------|------|-----------|----------------|
| 스캔 O / DB O | 유효 | UPDATE + last_seen 갱신 + weight 강화 | 동일 |
| 스캔 O / DB X | 신규 | INSERT(first_seen=현재) | 동일 |
| 스캔 X / DB O (auto) | 사라짐 | **weight--**, 0에서 soft-retire `state='stale'` | **DELETE** (참조된 엔드포인트는 보존) |
| 스캔 X / DB O (curated) | 지식 | **불가침** | **불가침** |

증분 모드는 즉시 은퇴가 아니라 감쇠라, 여러 스캔에 걸쳐 확증된 엣지는 일시적
누락(파싱 실패, 부분 스캔)을 **weight만큼 견딘다**. 노드는 활성 엣지가 하나도
남지 않을 때만 은퇴시켜 활성 뷰가 dangling 되지 않게 한다.

## 두 스캔 모드

- `codescan scan <path>` — **증분(기본)**. 재조정 + soft-retire. 그래프가 없으면
  결과적으로 full과 동일.
- `codescan scan <path> --full` (`--rebuild`) — **처음부터**. 이번 스캔에서 안 보인
  auto 행을 실제 삭제. curated 는 어느 모드에서도 생존.

## 스키마 에폭 = DB 파일명

하위 호환 불가한 스키마 변경은 **새 DB 파일**로 분리한다 (`codescan.db` →
`codescan-v2.db`, `AppPaths.DbFileName`). 파괴적 in-place 마이그레이션 대신 이전
파일을 그대로 남겨 롤백을 보장한다. 에폭 내부의 호환 가능한 증분 마이그레이션은
`PRAGMA user_version`(현재 2)으로 단계 처리한다.

## e2e 검증 (이 프로젝트 자체 스캔)

1. fresh → uv=2, 3357 노드 active.
2. 재스캔(증분) → 노드 **3358 그대로(중복 0)**, 클래스 식별자 1개 유지,
   auto 엣지 maxWeight 214→**428**(재정렬).
3. `--include .md`(증분) → active 1098 / **stale 2260**(총계 유지).
4. `--include .md --full` → 노드 3358→**1098**(stale 삭제), curated 생존.
5. 전체 재스캔 → 3358 전부 active 복원(무손실 재구성).

## Phase 로드맵

- **Phase 1 (완료)** — 식별자 project-scope화 + 생명주기 + reconcile(2모드) +
  curated 보존 + weight 강화 + soft-retire.
- **Phase 2 (완료)** — weight = 확증 스캔 수(스캔당 1회 강화) + 미관측 시 **감쇠**로
  일시적 누락 내성 + 고갈 시 은퇴 + 조회 **weight 순 재정렬** + 출력에 `×N` 노출.
- **Phase 3** — 메서드 식별자를 **시그니처 기반**으로 승격해 파일 이동/리네임에도
  노드가 살아남게(관계 리바인딩), 오버로드 구분.
