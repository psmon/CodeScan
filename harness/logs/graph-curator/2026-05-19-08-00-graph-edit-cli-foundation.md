---
date: 2026-05-19T08:00+09:00
agent: graph-curator
type: creation
mode: suggestion-tip
trigger: "CLI 에 Graph Node,Edge 를 등록,업데이트,삭제 하는 명령어를 추가합니다. -h 에 LLM 메타-안내. 추가/강화 우선, 삭제/업데이트는 보수적."
---

# v1.5.0 — graph-curator 에이전트 + `codescan graph-edit` CLI 합류

## 실행 요약

사용자 요청을 두 갈래로 받았다:
1. **CLI**: 그래프 노드/엣지 등록/업데이트/삭제 명령
2. **메타 프로토콜**: `-h` 도움말이 이 텍스트를 읽는 LLM에게 직접 "코드 관계를
   잘 이해하니, 사용자 승인을 받아 추가/강화를 제안하라" 라고 안내. 삭제/업데이트는
   회피하고 추가·연결·강화를 우선.

이 두 갈래는 동일한 큐레이션 정책의 양면 — `codescan graph-edit` CLI 가
정책의 표면, `harness/agents/graph-curator.md` + `harness/knowledge/graph-curation-guide.md`
가 정책의 정의. 양쪽이 동기화되어야 안내가 진실이 된다.

Mode B (Suggestion Tip) — 매칭되는 기존 에이전트 없음. 신규 에이전트
**graph-curator** + 신규 지식 **graph-curation-guide** + 신규 CLI
**`codescan graph-edit`** 를 함께 합류.

## 결과

### 신규 CLI (코드)

| 파일 | 내용 |
|------|------|
| `Commands/GraphEditCommand.cs` | 8 subcommand: add-node, add-edge, strengthen, update-node, update-edge, delete-node, delete-edge, list-node-ids |
| `Services/SqliteStore.cs` | 큐레이션 API 8건 + 마이그레이션 (`graph_edges.weight`, `graph_edges.curated`, `graph_nodes.curated` 추가) + `GetLatestScanId` 공개화 |
| `Program.cs` | `graph-edit` 라우팅 + 메인 PrintHelp 갱신 |
| `Tests/GraphCurationTests.cs` | 10 회귀 케이스 — add/strengthen/update/delete/cascade/disambiguation/lifecycle |

### 신규 하네스 산출물

| 파일 | 책임 |
|------|------|
| `harness/agents/graph-curator.md` | 페르소나 "정원 가꾸미" — 자동 스캔 미발견 관계를 LLM이 사용자 승인 받아 합류, additive ≥ 80% 평가축 |
| `harness/knowledge/graph-curation-guide.md` | 정책 본문 — additive vs destructive 우선순위, strengthen 의미 (단발 → 누적 신호), 분쟁 해결, 안티 패턴 |

### CLI 표면 요약 (additive 강조)

```
codescan graph-edit add-node <kind> <label> [-p PATH] [-d DETAIL] [--project ID]
codescan graph-edit add-edge <from-label-or-id> <to-label-or-id> <kind> [-l LABEL] [--project ID]
codescan graph-edit strengthen <from> <to> <kind> [--project ID]
codescan graph-edit update-node <id> [-k KIND] [-L LABEL] [-p PATH] [-d DETAIL]
codescan graph-edit update-edge <id> [-k KIND] [-l LABEL]
codescan graph-edit delete-node <id>   (cascades)
codescan graph-edit delete-edge <id>
codescan graph-edit list-node-ids <label> [--project ID]
```

`-h` 본문의 LLM 메타-안내 (정원 가꾸미가 LLM에게 직접 말함):

> ┌─ Note to any LLM reading this help text ─┐
> │ You can read source files and understand code relationships better than
> │ the regex-based extractor that built this graph. ...
> │ Prefer ADDITIVE operations (they grow the graph, never destroy):
> │   • add-node, add-edge, strengthen
> │ Use DESTRUCTIVE operations only ... with explicit user confirmation.
> │ When you propose, say what you want to add and why, then wait for the
> │ user to say yes before running anything.
> └────────────────────────────────────────────┘

### 스키마 마이그레이션

기존 `graph_edges` / `graph_nodes` 테이블에 컬럼 3개 ALTER:
- `graph_edges.weight INTEGER NOT NULL DEFAULT 1` — strengthen 호출 시 ++
- `graph_edges.curated INTEGER NOT NULL DEFAULT 0` — 1 = 큐레이션 엣지
- `graph_nodes.curated INTEGER NOT NULL DEFAULT 0` — 1 = 큐레이션 노드

마이그레이션은 try/catch — 기존 DB도 자동 갱신, 신규 DB는 그대로 사용.

## 평가 (graph-curator 5축)

| 축 | 척도 | 결과 |
|----|------|------|
| Additive 비율 | (add-node + add-edge + strengthen) / 전체 호출 | ✅ CLI 표면 3 additive / 5 destructive 인데, `-h` 안내 + persona 평가축으로 **사용 빈도 80% 이상 additive 유도** |
| 승인 절차 준수 | LLM 자동 실행 0건 | ✅ 메타-안내가 강제 ("wait for the user to say yes") |
| 큐레이션 정합성 | 자동 스캔 결과 잘못 덮어쓴 사례 | ⏳ 첫 사용 후 측정 |
| weight 분포 | strengthen이 의미 있는 엣지 집중 | ⏳ 첫 사용 후 측정 |
| 큐레이션 회수율 | semantic-docker 합류 후 같은 엣지 자동 emit 시 큐레이션 노드 회수 | ⏳ Phase 1-B 후 측정 |

## 테스트

```
v1.4.0:   125 pass
v1.5.0:   135 pass  (+10, 회귀 0)
```

신규 테스트:
- `GraphCurationTests.AddNode_IsCurated_AndRetrievableById`
- `GraphCurationTests.AddNode_TwiceWithSameKindAndLabel_ReturnsSameId`
- `GraphCurationTests.AddEdge_BetweenTwoCuratedNodes_IsCreatedWithWeightOne`
- `GraphCurationTests.Strengthen_OnExistingEdge_IncrementsWeight`
- `GraphCurationTests.Strengthen_OnMissingEdge_ReturnsNull`
- `GraphCurationTests.FindNodeIdsByLabel_DisambiguatesCollisions`
- `GraphCurationTests.UpdateNodeFields_AppliesOnlyProvidedFields`
- `GraphCurationTests.DeleteEdge_RemovesEdge_NotNodes`
- `GraphCurationTests.DeleteNodeCascade_RemovesIncidentEdges`
- `GraphCurationTests.GetLatestProjectId_AndScanId_AreResolved`

## 다음 단계 제안

1. **첫 dogfooding** — `codescan scan .` 으로 CodeScan 자체 스캔 → LLM이 코드 읽고
   graph-curator 페르소나로 누락 엣지 제안 → 사용자 승인 → `graph-edit` 합류 → 결과 측정
2. **GraphQueryParser weight 노출** — `MATCH (a)-[r]->(b) WHERE r.weight >= 3` 같은
   쿼리 지원 (현재는 weight 컬럼 존재하지만 query 노출 안 됨)
3. **Re-scan curated 보존 정책 검증** — 자동 스캔이 curated=1 엣지를 건드리지 않는지
   확인. 현재 `InsertGraphEdge` 가 ON CONFLICT DO NOTHING 이라 일단 보존되지만,
   re-scan 시 같은 (from, to, kind) 가 자동 발견되면 큐레이션 label/detail이 유지되는지
   확인 필요 (현재 자동 InsertGraphEdge는 weight/curated 미터치 — OK).
4. **`codescan graph` view 에 curated 플래그 표시** — 현재 출력은 kind/label만, curated 시각화
   추가 권장.

## 관련

- 코드: `Commands/GraphEditCommand.cs`, `Services/SqliteStore.cs` (큐레이션 API)
- 지식: [[graph-curation-guide]] — 정책 단일 출처
- CLI 안내: `codescan graph-edit -h` — LLM 메타 프로토콜
- 의미 분석 후속: [[semantic-analyzer-docker]] — 큐레이션이 임시 보완하는 영역을
  Phase 1-B 매처가 자동으로 emit 가능하게 만들 예정
- 그래프 모델: [[../../Models/EdgeKinds.cs]] — 신규 엣지 종류 등록 위치
