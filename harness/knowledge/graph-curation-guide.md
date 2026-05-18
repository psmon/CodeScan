# Graph Curation Guide

> `codescan graph-edit` 가 노드/엣지 합류를 다루는 정책. LLM이 사용자 승인을
> 받아 그래프를 강화할 때 따라야 하는 절차와 안전 규칙을 단일 출처로 둔다.

## 큐레이션이 풀려는 문제

자동 스캔 (regex / semantic-docker) 은 **표면**을 본다 — 토큰, 키워드,
타입 매개변수. 표면 너머의 관계는 코드를 **읽고 이해해야** 발견된다:

- 인터페이스 매개변수의 구현체 — `void Send(IClient c)` 의 `c`가 런타임에 어떤 구현인지
- 도메인 동의어 — `User` 와 `Account` 가 같은 책임을 지는 경우
- 협력 관계 — 직접 호출은 없지만 같은 워크플로우에 참여하는 컴포넌트
- 의미적 의존성 — 데이터 구조가 다른 모듈의 표현 방식을 강제하는 경우

이런 관계는 정규식이 잡을 수 없고 의미 분석 도커도 도구마다 한계가 있다.
**LLM이 코드를 읽으면 알 수 있는** 관계가 핵심 큐레이션 대상.

## 큐레이션 vs 자동 스캔

| 항목 | 자동 스캔 | 큐레이션 |
|------|----------|---------|
| 출처 | regex / semantic-docker | 사람 + LLM 협업 |
| 트리거 | `codescan scan` 시 자동 | `codescan graph-edit` 명시 호출 |
| `curated` 플래그 | 0 | **1** |
| `weight` (엣지) | 1 (자동 스캔이 weight 갱신 안 함) | 1 (add-edge) → ++ (strengthen) |
| 재스캔 시 운명 | 새 자동 결과로 교체 | 정책에 따라 보존 (curated=1 우선) |

## 우선순위 — Additive > Destructive

`codescan graph-edit -h` 가 LLM에게 안내하는 핵심 원칙. 이 우선순위는 도구
설계의 본질이지 사용자 편의가 아니다.

```
1. ADD     — 자동 스캔이 놓친 노드/엣지를 새로 합류 (정원에 꽃 심기)
2. STRENGTHEN — 기존 엣지의 weight 증가 (정원의 꽃에 물주기)
3. UPDATE  — 자동 스캔이 만든 잘못된 노드/엣지를 보정 (가지치기 — 보수적)
4. DELETE  — 잘못된 엣지/노드 제거 (꽃 뽑기 — 가장 보수적)
```

LLM은 **항상 1, 2를 먼저 제안**한다. 3, 4는 자동 스캔 결과가 명백히 틀린
경우에만 제안하고 사용자에게 "이 자동 결과를 덮어쓰겠습니다 — 동의?" 형태로
확인을 받는다.

이유: 그래프는 **누적 자산**이다. 잘못된 엣지가 1건 추가되어도 weight=1로
신호가 약하지만, 잘못된 삭제는 그 정보를 영원히 잃는다. 추가의 비용은 낮고
삭제의 비용은 높다.

## 표준 워크플로우 (LLM-사용자 협업)

```
1. 사용자: "이 모듈의 관계 좀 봐줘"
2. LLM: 코드 읽기 + 자동 스캔 결과 확인
   $ codescan query "MATCH (n:class) WHERE n.label CONTAINS '...' RETURN n"
3. LLM: 누락 제안 정리
   "다음 3개 엣지가 빠진 것 같습니다:
    - WorldActor -[uses]-> ActorSystem  (line 5에서 직접 참조)
    - WorldActor -[creates]-> Props      (line 33에서 람다 안 new)
    - PersonActor -[receives]-> SayHello (Receive<T> handler)
    추가할까요?"
4. 사용자: "1, 2번 좋다. 3번은 semantic-docker가 받을 영역이니 미뤄"
5. LLM: 승인된 항목만 실행
   $ codescan graph-edit add-edge WorldActor ActorSystem uses
   $ codescan graph-edit add-edge WorldActor Props creates
6. LLM: 검증
   $ codescan graph "WorldActor"
```

**금지된 시나리오**:
- 사용자 승인 없이 graph-edit 실행
- 자동 스캔 결과를 묻지 않고 update/delete
- 같은 엣지를 N번 strengthen해서 weight를 인위적으로 부풀리기

## strengthen 의 의미

`strengthen` 은 단순 카운터 증가가 아니라 **신호 누적**:

- weight=1: 단발성 관찰. "이 관계가 코드 어딘가에 한 번 나온다."
- weight=2~3: 반복 관찰. "여러 위치에서 같은 관계를 확인한다."
- weight≥5: 강한 신호. "이 관계는 모듈의 핵심 결합점이다."

미래 그래프 쿼리:

```
MATCH (a)-[r]->(b) WHERE r.weight >= 3 RETURN a, b
   → 핵심 결합점만 추출 (단발 false positive 필터링)
```

이 쿼리 패턴은 `codescan query` 가 weight 필드를 지원하면 즉시 활용 가능.
현재는 weight가 저장만 되고 쿼리 노출은 미적용 — 후속 작업.

## 분쟁 해결

같은 (from, to, kind) 엣지에 대해 자동 스캔과 큐레이션이 다른 label/detail을
가지는 경우:

1. **upsert 정책**: `UpsertCuratedEdge` 가 호출되면 label은 새 값으로 교체,
   curated=1 set. 단 weight 는 보존 (기존 자동 스캔이 1이었다면 1 유지,
   strengthen 호출 시 ++).
2. **재스캔 시**: 자동 스캔의 `InsertGraphEdge` 는 ON CONFLICT DO NOTHING.
   즉 curated=1 엣지는 자동 스캔이 건드리지 않음. 같은 (from, to, kind) 가
   있다면 큐레이션 결과 보존.
3. **충돌 신호**: 자동 스캔이 발견한 엣지를 큐레이션이 update로 덮어쓰면
   `curated=1` 마킹. 다음 자동 스캔이 같은 엣지를 또 발견해도 보존.

## 안티 패턴

| 안티 패턴 | 왜 나쁜가 |
|-----------|----------|
| LLM이 사용자 승인 없이 add-edge 자동 실행 | -h 안내 위반 + 신뢰 깨짐 |
| 자동 스캔 결과를 모두 delete 후 큐레이션으로 재구성 | 자동 스캔의 가치 무효화, 재스캔마다 반복 작업 |
| label만 다른 같은 (from, to, kind) 엣지를 strengthen 대신 add-edge로 추가 | upsert 충돌 — UpsertCuratedEdge가 label 교체 + 같은 엣지 |
| weight 부풀리기 (같은 관계를 N번 strengthen) | 신호 의미 왜곡 — 진짜 핵심 결합점이 묻힘 |
| destructive operation으로 정원을 "정리"하기 | 그래프는 누적 자산. 정리가 아니라 추가가 본업 |

## 관련

- CLI: `codescan graph-edit -h`
- 코드: `Services/SqliteStore.cs` (UpsertCuratedNode / UpsertCuratedEdge / StrengthenEdge)
- 스키마: `graph_nodes.curated` / `graph_edges.curated` / `graph_edges.weight`
- 에이전트: [[graph-curator]]
- 후속 의미 분석: [[semantic-analyzer-docker]] — 자동 스캔이 잡지 못하는 관계를
  semantic-docker로 옮기면 큐레이션 노드가 회수될 수 있음
- 그래프 모델: `Models/EdgeKinds.cs` — 신규 엣지 종류 등록 시 const 추가
