---
name: actor-graph-extension
type: engine
triggers:
  - "액터 그래프 확장"
  - "actor edge 검증"
  - "spawn 매트릭스 확인"
description: 3개 액터 툴킷 fixture(csharp-akka/java-akka/kotlin-pekko)를 입력으로 (1) 현재 정규식 결과를 매트릭스로 측정 (2) 신규 엣지 타입(spawns_child/receives_message/sends_message_to)이 의미 분석으로 추가되어야 할 케이스를 식별하는 워크플로우.
participants:
  - actor-toolkit-scout
  - semantic-bridge-architect
  - language-analyzer-keeper
---

# Actor Graph Extension 워크플로우

> 목적: 액터 hierarchy의 부모-자식/메시지 dispatch 관계가 그래프에 정확히 표현되는지 측정하고,
> 의미 분석 도커로 무엇이 추가되어야 합격인지 합격선을 명세한다.

## 트리거

- "액터 그래프 확장" / "actor edge 검증" / "spawn 매트릭스 확인"
- 또는 정기 점검 (의미 분석 도커 PoC 진척 시)

## 절차

1. **액터 추적자 진입** ([[actor-toolkit-scout]])
   - 3개 fixture 도커 빌드/실행 검증
     ```
     docker run --rm hello-akka          # csharp-akka
     docker run --rm hello-akka-java     # java-akka
     docker run --rm hello-pekko-kotlin  # kotlin-pekko
     ```
   - codescan 3개 프로젝트 스캔
   - 매트릭스 산출 (inherits / creates / uses_type / imports)
   - [[actor-model-cross-toolkit]] 의 "정규식 한계 매트릭스" 갱신

2. **다국어 정원사 협력** ([[language-analyzer-keeper]])
   - 정규식으로 풀 수 있는 케이스가 있는지 재검토
   - 예: 액터 베이스 클래스 상속(`extends AbstractActor`) → 정규식으로 충분
   - 신규 회귀 발견 시 [[language-analyzer-patterns]] 업데이트

3. **컴파일러 가교 설계자 협력** ([[semantic-bridge-architect]])
   - 정규식으로 풀 수 없는 케이스 식별:
     - 부모-자식 spawn (4가지 표현 모두 의미 손실)
     - Receive/onMessage dispatch table
     - Tell 데이터 흐름
   - 도커 이미지별 매처 사양 작성 (의사코드)
   - PoC 우선순위 결정 (Pekko Typed → Akka.NET → Akka Classic)

4. **합격선 명시**
   - 의미 분석 도커 도입 후 매트릭스 합격 기준:
     - `spawns_child` 엣지: 12/12 (3 툴킷 × 4 자식 액터 평균)
     - `receives_message` 엣지: 각 액터의 Receive/onMessage 핸들러 수와 일치
     - `inherits` 정확도: 현재 ✅ (5/5 csharp, 5/5 java) — Kotlin은 회귀(R1) 수정 필요

5. **로그 기록** (필수)
   - `harness/logs/actor-graph-extension/{yyyy-MM-dd-HH-mm}-matrix.md`
   - 3-툴킷 매트릭스 + 신규 엣지 후보 + 매처 의사코드 + PoC 다음 단계

## 산출물

- 3-툴킷 매트릭스 표 (정규식 결과)
- 도커 이미지 PoC 우선순위 (어느 툴킷부터)
- 신규 그래프 엣지 후보 검토 결과
- (의미 분석 도커 도입 후) 정규식 vs 의미 분석 비교 매트릭스

## 중단 조건

- 3개 fixture 중 하나라도 도커 빌드 실패 → 그 fixture 정상화 우선
- 정규식 매트릭스가 기존 측정 대비 후퇴 → [[language-analyzer-keeper]] 회귀 점검 우선

## 관련

- 직접 입력: `TestSample/csharp-akka/`, `TestSample/java-akka/`, `TestSample/kotlin-pekko/`
- 지식: [[actor-model-cross-toolkit]], [[semantic-analyzer-docker]], [[language-analyzer-patterns]]
- 후속 엔진: (의미 분석 도커 PoC 진행 시) `semantic-docker-rollout.md`
