---
name: test-audit
type: engine
triggers:
  - "테스트 감사"
  - "test 감사"
  - "테스트 매트릭스 확인"
description: SourceAnalyzer/CommentExtractor의 언어 × 케이스 매트릭스를 점검하고, 누락 셀에 대한 테스트 시안을 도출하는 워크플로우.
participants:
  - test-sentinel
  - regex-safety-guard
  - aot-compatibility-scout
---

# Test Audit 워크플로우

> 목적: 7개 언어 × N개 케이스 매트릭스를 비어있지 않게 유지한다.

## 트리거

- "테스트 감사" / "test 감사" / "테스트 매트릭스 확인"
- 또는 정기 점검 (예: PR 머지 직전, 릴리즈 전)

## 절차

1. **시험관 진입 (test-sentinel)**
   - `dotnet test` 실행
   - `Tests/SourceAnalyzerTests.cs`, `Tests/CommentExtractorTests.cs`에서 `[Theory]`/`[Fact]` 이름 수집
   - 언어 × 케이스 매트릭스 작성
   - 누락 셀 식별

2. **수문장 협력 (regex-safety-guard)**
   - 최근 변경된 `[GeneratedRegex]` 목록 추출 (git log -- '*Analyzer*.cs')
   - 변경된 패턴별 테스트 존재 여부 검증
   - 백트래킹 위험 패턴 동시 점검

3. **정찰병 협력 (aot-compatibility-scout)**
   - 테스트 코드 안에서 AOT 위반 API 사용 여부 확인 (운영 코드 외)
   - 통합 테스트가 AOT 빌드와 분리되어 있는지 확인

4. **종합 리포트**
   - 누락 셀 목록 + 우선순위
   - 위험 패턴 + 대응 테스트 시안
   - AOT 충돌 가능성 표시

5. **로그 기록** (필수)
   - `harness/logs/test-audit/{yyyy-MM-dd-HH-mm}-audit.md`
   - 매트릭스 표 포함, 평가 결과 포함

## 산출물

- 언어 × 케이스 매트릭스 표
- 누락 셀별 테스트 시안 (코드까지 자동 생성하지 않음 — 시안만)
- 위험도 정렬된 액션 아이템

## 중단 조건

- `dotnet test`에서 fail 발생 → 매트릭스 작성 전에 실패 원인부터 보고
- 7개 언어 중 어느 하나라도 `[Theory]`/`[Fact]` 0건 → Critical로 표시
