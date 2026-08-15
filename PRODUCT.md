# Product

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop nativo (WPF, .NET 10). Não é web/iOS/Android/adaptive — nenhum desses valores descreve este produto; registrado aqui por precisão, não por conformidade ao enum padrão do esquema.

## Users

Jogadores de FiveM (GTA V Legacy) em PC Windows que querem diagnosticar e otimizar o desempenho do jogo com segurança. Conhecimento técnico varia de iniciante ("só quero melhorar o FPS") a entusiasta que acompanha cada ação aplicada. Uso é ocasional/eventual — antes de jogar ou quando o desempenho piora — não uma ferramenta aberta o tempo todo.

## Product Purpose

FiveMCleaner diagnostica, limpa e otimiza a instalação do FiveM/GTA V Legacy de forma transparente e reversível. Sucesso é o usuário confiar no que foi feito — cada ação é visível, documentada e reversível — e não uma promessa de FPS não verificável.

## Positioning

Diferente de "otimizadores" genéricos de Windows ou dicas de fórum: cada ação tem escopo tipado, pré-condições, aplicação verificada e rollback; nunca desativa Defender/Firewall/Update, nunca promete FPS universal, nunca aplica tweaks perigosos (afinidade fixa, prioridade Realtime, SMT desligado) como se fossem seguros. A confiança vem da transparência e da reversibilidade, não da agressividade.

## Operating Context

Aplicativo desktop Windows, tipicamente executado antes/depois de sessões de FiveM. Fluxo típico: abrir o app → ver diagnóstico → escolher perfil (Leve/Médio/Agressivo) → revisar e confirmar o plano → acompanhar execução → ver resultado. Requer elevação pontual via broker allowlisted para algumas ações administrativas. Convive com tema claro/escuro/sistema e localização (pt-BR, en, es).

## Capabilities and Constraints

Diagnóstico de hardware/software, perfis versionados, execução transacional com journal/rollback, histórico de execuções, conta opcional (Firebase), telemetria opt-in, atualizador integrado. Suporta apenas GTAV Legacy (Enhanced é bloqueado até existir adaptador próprio). Não há overlay/medição de FPS dentro do jogo; o benchmark é o oficial standalone do GTA V, fora de sessão. Limites técnicos completos em `docs/architecture.md` e `docs/safety.md`.

## Brand Commitments

Nome "FiveMCleaner". Logo estabelecido (`src/FiveMCleaner.App/Assets/FiveMCleaner.png`): "5M" em laranja com um rasto/sparkle que sugere limpeza, "M" em metal escovado prateado, sobre grafite quase preto. Laranja como acento é um compromisso de marca real e deve continuar sendo a cor de identidade em qualquer redesign; o metal escovado do "M" é material de marca tão real quanto a cor.

## Evidence on Hand

Nenhum dado de terceiros, depoimento, benchmark ou caso de uso fabricado deve aparecer na interface. Toda métrica exibida vem de diagnóstico real do sistema do usuário; quando um dado não está disponível, a interface deve dizer isso — nunca estimar ou inventar.

## Product Principles

- Transparência sobre agressividade: mostrar o que vai mudar antes de mudar.
- Reversibilidade como recurso de primeira classe, não um detalhe técnico escondido.
- Precisão técnica sem intimidar: linguagem de instrumento/diagnóstico, não jargão de marketing.
- Nunca prometer ganho de desempenho sem evidência reproduzível.
- Consistência entre os três perfis: o usuário escolhe intensidade, não uma lista arbitrária de tweaks.

## Accessibility & Inclusion

Preservar contraste, navegação por teclado, foco visível, DPI/escala do Windows e o mecanismo de localização existente (pt-BR, en, es) em qualquer novo componente visual.
