이제 이 프로젝트의 핵심인 단순하게 reg가 아닌 의존성을 파악하는지 여부를 보강하기위해 다음 빌드가능한 샘플 프로젝트를 실제 만든후
codescan 을해 graph 탐색이 가능한지? e2e 테스트를 강화하려고함

샘플코드는 의존성이 적은 콘솔형 프로젝트 코드로 해당언어에서 비교적 모던한 버전을 채택할것
- Hello World 콘솔
- 의존성 테스트를 위해 OOP 로 World 구성 Person 을 구성해 Speek 함수사용
- Person은 En,KR,JA 3종류 언어가능자가 있으며 3개국어로 Hello 하면 Person이 각각의 언어로 World 로 응답하는 심플 프로젝트
- 언어별 git iggone를 추가해 불필요한 파일이 커밋되지않도록 (빌드에 필요한 최소파일)

C#을 포함 다음 언어 공식 analyzer 를 강화 지원약속을 지켜야함

```
생성 프로젝트 위치 : TestSample 하위에 프로젝트별
도커빌드를 활용할수 있으니 빌드 검증은 도커빌드를 통해 수행하고 빌드산출물이 스캔에 필요한경우 
로컬디렉토리에 copy 해 codescan이 참조할수 있도록 할것 ( 언어적 특성에따라 전략적 수행)


코드스캔 지원 스펙

C#	.cs	class / struct / record / interface	접근자 + 반환형 + 이름	using, 상속/인터페이스, new, 타입 사용
Java	.java	class / interface / enum	접근자 + 반환형 + 이름	import, extends/implements, new, 타입 사용
Kotlin	.kt, .kts	class / object / data class / sealed class	fun / suspend fun	import, 기반 타입, 생성자/타입 사용
JavaScript	.js, .jsx	class	function / arrow / const / export	import, extends/implements 류 힌트, new, 타입 유사 사용
TypeScript	.ts, .tsx	class	function / arrow / const / export	import, extends/implements, new, 타입 어노테이션
PHP	.php	class / interface / trait	function	use, extends/implements, new, 타입 힌트
Python	.py	class (들여쓰기 기반)	def / async def (들여쓰기 기반)	import, 베이스 클래스, 생성자 유사 호출
Go	.go	type struct/interface	그래프 의존성 스캔만	import, 생성자/타입 사용
Rust	.rs	struct / enum / trait	그래프 의존성 스캔만	use, 연관 생성자/타입 사용
C/C++	.c, .cc, .cpp, .cxx, .h, .hpp, .hh, .hxx	class / struct	그래프 의존성 스캔만	include, 상속, new, 타입 사용

그래프 엣지 규칙
구조 엣지:

엣지	의미
project -[contains]-> directory/file	프로젝트 파일 트리
directory -[contains]-> directory/file	디렉토리 파일 트리
file -[contains]-> class	소스 파일에서 발견된 클래스/타입
class/file -[defines]-> method	메서드/함수 정의
file -[has_comment]-> comment	소스 파일에서 발견된 주석 블록
author -[authored]-> method	git blame 최종 작성자 관계
project -[documents]-> doc	자동 발견된 프로젝트 문서
의존성 힌트 엣지:

엣지	출처
file/class -[imports]-> module	using, import, use, #include
class -[inherits_or_implements]-> type	베이스 클래스 / 인터페이스 / trait 스타일 선언
class -[creates]-> type	생성자 또는 new Type() 같은 생성자 유사 호출
class -[uses_type]-> type	정규식 전략이 감지한 타입 어노테이션, 필드, 매개변수, 반환, 지역 선언
의존성 그래프는 의도적으로 하이브리드입니다. CodeScan은 먼저 언어 중립적 정규식 전략을 사용하므로 프로젝트가 빌드되지 않아도 그래프 엣지가 존재합니다. 동시에 프로젝트 메타데이터로 의미 분석 가능성을 탐지합니다:

언어	의미 분석 프로브
C#	향후 Roslyn 분석을 위한 .sln, .csproj
Java	향후 JDT/Spoon 분석을 위한 pom.xml, build.gradle, build.gradle.kts
TypeScript/JavaScript	향후 TypeScript Compiler API 분석을 위한 tsconfig.json, jsconfig.json
Go	향후 go/packages 분석을 위한 go.mod, go.work
Rust	향후 rust-analyzer / Cargo 메타데이터 분석을 위한 Cargo.toml
C/C++	향후 Clang LibTooling 분석을 위한 compile_commands.json
```