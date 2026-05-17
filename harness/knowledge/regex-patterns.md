---
name: regex-patterns
type: knowledge
domain: parser
description: CodeScan의 7개 언어 정규식 패턴 설계 원칙과 AOT 규칙.
---

# 정규식 패턴 가이드 (CodeScan)

## 핵심 원칙

1. **모든 regex는 `[GeneratedRegex]` 어트리뷰트로 선언한다** — AOT 안전.
2. **`new Regex(...)` / `Regex.Match(string, string)` 직접 호출 금지** — 트리밍/AOT 위험.
3. **백트래킹 폭발 패턴 금지** — 중첩 수량자(`(.+)+`, `(a|a)*`), greedy + 광범위 alternation.
4. **언어별 모듈화** — 각 언어의 키워드/주석 규칙을 분리해 한 곳에서 7개 언어 분기 금지.

## 7개 언어 별 키워드 표

| 언어 | 클래스 키워드 | 메서드 시그니처 단서 | 주석 |
|------|--------------|---------------------|------|
| C# | `class`, `record`, `struct`, `interface` | 접근자 + 반환형 + 식별자 + `(` | `//`, `/* */`, `///` |
| Java | `class`, `interface`, `enum`, `record` | 접근자 + 반환형 + 식별자 + `(` | `//`, `/* */`, `/** */` |
| Kotlin | `class`, `object`, `interface` | `fun` 키워드 | `//`, `/* */`, `/** */` |
| JS | `class`, `function` | `function` / 메서드 단축 / 화살표 | `//`, `/* */`, `/** */` |
| TS | JS + 타입 어노테이션 | JS + 시그니처 + `:` 반환형 | `//`, `/* */`, `/** */` |
| PHP | `class`, `interface`, `trait` | `function` 키워드 | `//`, `#`, `/* */`, `/** */` |
| Python | `class` | `def` 키워드 | `#`, `"""`, `'''` |

## 엣지 케이스 체크리스트

- [ ] 제네릭 타입 (`List<Map<K, V>>`) — 꺾쇠 균형
- [ ] 중첩 클래스 — 들여쓰기 또는 중괄호 깊이
- [ ] 람다/익명 함수 — 메서드로 오인 방지
- [ ] 표현식 본문 멤버 (C# `=>`) — 메서드 인식
- [ ] 한 줄에 다중 정의 — 분리
- [ ] 어트리뷰트/데코레이터/어노테이션이 시그니처 직전에 붙는 경우
- [ ] 주석 안의 키워드 — 토큰화 후 매칭으로 피하거나 주석 제외 사전 처리

## AOT 위반 패턴 (금지)

```csharp
// 금지
var r = new Regex(@"...");
Regex.Match(input, @"...");
Regex.IsMatch(input, @"...");

// 권장
[GeneratedRegex(@"...")]
private static partial Regex MyPattern();
```

## 백트래킹 위험 패턴 (금지)

```
(.+)+             // 중첩 수량자
(a|a)*            // 동등 대안 반복
([a-z]+)*\d       // greedy + 광범위
```

대안: 소유 수량자(.NET 미지원이므로) → **명시적 경계 + 비욕심**(`.*?`) + **앵커**(`^/$`).

## 참고

- 패턴 변경 시 `Tests/SourceAnalyzerTests.cs`, `Tests/CommentExtractorTests.cs`에 케이스 추가.
- 신규 언어 추가 시 위 표와 엣지 케이스 매트릭스를 함께 갱신.
