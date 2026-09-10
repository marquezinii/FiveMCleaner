# Design system — Ralven

Este arquivo traduz a identidade oficial para as superfícies reais do produto. Os arquivos-fonte, exports, tokens e regras de procedência vivem em [`assets/brand`](assets/brand/).

## Direção

Ralven é precisa, sóbria e prática. A interface usa profundidade discreta, hierarquia clara e materiais grafite; não usa estética gamer genérica, neon, glow, partículas ou cor decorativa.

Princípios:

- desempenho sem promessas universais;
- controle antes da execução;
- segurança e reversibilidade visíveis;
- poucos níveis de superfície e navegação direta;
- informação real acima de decoração.

## Cor

Paleta oficial:

| Token | Valor | Uso |
| --- | --- | --- |
| Charcoal | `#0A0A0B` | canvas canônico escuro |
| Graphite | `#111214` | rail e superfícies rebaixadas |
| Dark gray | `#1D1E21` | painéis e controles |
| Gray | `#2D2E33` | hover, seleção e bordas fortes |
| Light gray | `#A6A7AC` | texto secundário e traços claros |
| White | `#FFFFFF` | texto principal e ação primária |

O tema escuro é a expressão canônica. O tema claro é uma derivação acessível em neutros, não uma nova identidade. Cores semânticas de sucesso, atenção, erro e informação continuam permitidas somente para comunicar estado; nunca substituem a paleta de marca.

Regras:

- texto normal mantém contraste mínimo de 4,5:1;
- foco de teclado é sempre visível e não depende apenas de cor;
- selecionado, erro e sucesso têm uma segunda pista de forma ou texto;
- gradientes são sutis, neutros e nunca reduzem legibilidade.

## Tipografia

Inter é a família oficial e é incorporada ao aplicativo e ao site a partir da distribuição oficial versionada na biblioteca de marca. Fallbacks existem apenas para falha de carregamento.

| Estilo | Tamanho sugerido | Peso |
| --- | --- | --- |
| Display | 32–40 | Bold |
| Título de página | 24–28 | SemiBold |
| Título de seção | 17–20 | SemiBold |
| Corpo | 14–16 | Regular |
| Apoio | 12–14 | Regular |
| Overline | 11–12 | SemiBold, tracking discreto |

Números técnicos podem usar algarismos tabulares da própria Inter. Fonte monoespaçada é reservada para hashes, paths e saída técnica.

## Forma e profundidade

- raios de 6, 10, 14 e 18 px;
- bordas de 1 px em neutro discreto;
- elevação por pequena diferença tonal e sombra curta;
- no máximo uma superfície contida dentro de outra;
- ícones lineares, minimalistas, com cantos arredondados e traço uniforme.

O símbolo oficial é raster de origem aprovada. Não deve ser redesenhado, vetorizado automaticamente, distorcido, recolorido ou aplicado sobre fundo complexo. Até existir um master vetorial aprovado pelo designer, os exports raster derivados são a fonte operacional.

## Layout

A janela mantém rail lateral compacto e páginas com cabeçalho, ação principal e blocos irmãos. Os mockups anexados são direção visual, não um contrato de funcionalidades: Drivers, Rede, ReShade, loja, avaliações e outros módulos só aparecem quando existirem de verdade.

As áreas atuais são:

- Visão geral;
- Sistema;
- Aplicativos;
- FiveM;
- Histórico;
- Configurações.

Espaçamento base: 4, 8, 12, 16, 20, 24, 32 e 40 px. Conteúdo operacional deve continuar utilizável em 1040×620, DPI alto e zoom do Windows.

## Componentes

- Ação primária: fundo branco no tema escuro, texto carvão, foco explícito.
- Ação secundária: superfície grafite, borda discreta.
- Perigo: semântico vermelho apenas onde existe consequência destrutiva.
- Cartões: um propósito, título curto, descrição útil e ação direta.
- Formulários: rótulo persistente, ajuda concisa e erro próximo ao campo.
- Progresso: etapas e estado reais; nunca animação infinita para trabalho concluído.
- Estados vazios: explicam o que falta e oferecem uma próxima ação válida.

## Movimento

Movimento reforça causalidade, não identidade. Transições ficam entre 100 e 240 ms, respeitam a preferência de reduzir animações do Windows e evitam deslocar elementos sob o ponteiro. Sem parallax, pulso contínuo, bloom ou animação decorativa do logotipo.

## Voz

Frases curtas, concretas e honestas. Preferir “Revisar plano”, “Otimização concluída” e “Não foi possível verificar” a slogans dentro de fluxos operacionais. “Seu Windows, sob controle.” pode aparecer em superfícies institucionais; não é garantia de resultado.

## Verificação

Mudanças visuais precisam passar por:

1. contratos de tokens e localização;
2. captura dark e light das páginas reais;
3. navegação por teclado, foco e redução de movimento;
4. inspeção em 100% e 150% de escala;
5. confirmação de que nenhum recurso ou resultado foi inventado.
