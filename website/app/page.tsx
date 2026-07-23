"use client";

import { useEffect, useState } from "react";
import Image from "next/image";

type Language = "pt" | "en";

const DOWNLOAD_URL =
  "https://github.com/marquezinii/FiveMCleaner/releases/latest/download/FiveMCleaner-Setup-latest-win-x64.exe";
const GITHUB_URL = "https://github.com/marquezinii/FiveMCleaner";

const copy = {
  pt: {
    skip: "Pular para o conteúdo",
    languageLabel: "Selecionar idioma",
    navLabel: "Navegação principal",
    highlightsLabel: "Destaques",
    summaryLabel: "Resumo do produto",
    platformsLabel: "Plataformas preservadas",
    nav: {
      profiles: "Perfis",
      howItWorks: "Como funciona",
      safety: "Segurança",
      faq: "Dúvidas",
    },
    headerDownload: "Baixar",
    hero: {
      eyebrow: "CENTRAL OFICIAL DE DOWNLOAD",
      titleStart: "Seu PC mais preparado.",
      titleAccent: "Seu FiveM mais fluido.",
      body: "Uma experiência premium para preparar o Windows, o FiveM e o GTA V Legacy com decisões técnicas claras, progresso ao vivo e mudanças que podem ser revisadas.",
      download: "Download do instalador",
      github: "Ver código no GitHub",
      releaseNote: "Download pelo GitHub Releases · sem cadastro",
      included: "Runtime incluído",
      windows: "Windows 10 e 11",
      rollback: "Rollback disponível",
      releaseKicker: "RELEASE OFICIAL",
      releaseTitle: "Instalação simples. Sem dependências extras.",
      releaseBody: "O instalador inclui o runtime necessário e abre a versão estável mais recente, publicada na origem oficial do projeto.",
      releaseLink: "Abrir download seguro",
    },
    preview: {
      label: "PRÉVIA DA INTERFACE",
      appStatus: "Sistema pronto",
      title: "Seu PC, o plano certo",
      subtitle: "Diagnóstico local concluído",
      ringTop: "PRONTO",
      ringBottom: "para otimizar",
      profileLabel: "Perfil selecionado",
      light: "Leve",
      medium: "Médio",
      aggressive: "Agressivo",
      recommended: "RECOMENDADO",
      progressTitle: "Preparando otimização segura",
      progressDetail: "Validando configurações atuais…",
      detectedGpu: "CPU e GPU identificadas",
      detectedGame: "FiveM detectado",
      local: "Processamento local",
    },
    quickFacts: [
      ["01 clique", "para iniciar"],
      ["03 perfis", "decididos pelo app"],
      ["Local", "sem telemetria"],
      ["Reversível", "com snapshots"],
    ],
    profiles: {
      eyebrow: "TRÊS PRIORIDADES, UMA ESCOLHA SIMPLES",
      title: "Escolha o perfil. O app cuida do restante.",
      intro: "Nada de dezenas de caixas confusas. Cada perfil usa um catálogo seguro de ações e só altera o que realmente precisa de ajuste.",
      items: [
        {
          number: "01",
          name: "Leve",
          summary: "Mais suavidade sem sacrificar a aparência.",
          ideal: "Ideal para PCs atuais que já rodam bem.",
          bullets: [
            "Preserva a qualidade visual",
            "Corrige preferências essenciais do Windows",
            "Remove apenas arquivos temporários seguros",
          ],
        },
        {
          number: "02",
          name: "Médio",
          summary: "O melhor equilíbrio entre imagem e resposta.",
          ideal: "Recomendado para a maioria dos jogadores.",
          bullets: [
            "Equilibra sistema, FiveM e GTA V",
            "Prioriza a GPU correta quando necessário",
            "Mantém serviços e proteções críticas",
          ],
          badge: "RECOMENDADO",
        },
        {
          number: "03",
          name: "Agressivo",
          summary: "Prioridade máxima para desempenho.",
          ideal: "Para hardware de entrada ou muito limitado.",
          bullets: [
            "Reduz mais efeitos visuais dispensáveis",
            "Aplica o conjunto de desempenho mais forte",
            "Continua preservando segurança e rollback",
          ],
        },
      ],
      estimateTitle: "Estimativa honesta, baseada no seu PC",
      estimateBody: "O FiveMCleaner usa o hardware e o estado detectado para contextualizar o potencial de cada perfil. Quando não existe um benchmark real, ele informa “ganho não medido” — nunca inventa FPS.",
      estimateTag: "SEM NÚMEROS FALSOS",
    },
    process: {
      eyebrow: "SIMPLES POR FORA, CUIDADOSO POR DENTRO",
      title: "Você acompanha tudo. Passo a passo.",
      intro: "A barra de progresso mostra a ação atual, o tempo decorrido e uma previsão de término. Se algo já estiver correto, o app confirma e segue adiante.",
      steps: [
        {
          number: "01",
          title: "Diagnostica",
          text: "Identifica processador, placa de vídeo, memória, Windows, FiveM e GTA V.",
        },
        {
          number: "02",
          title: "Planeja",
          text: "Monta automaticamente o plano compatível com o perfil escolhido.",
        },
        {
          number: "03",
          title: "Protege e aplica",
          text: "Registra o estado anterior e executa apenas as mudanças necessárias.",
        },
        {
          number: "04",
          title: "Resume",
          text: "Entrega um histórico claro do que mudou, do que foi ignorado e do que pode ser desfeito.",
        },
      ],
    },
    transparency: {
      eyebrow: "CONTROLE SEM COMPLEXIDADE",
      title: "Nada escondido atrás do botão.",
      body: "O FiveMCleaner foi projetado para automatizar trabalho técnico sem transformar o Windows em uma caixa-preta.",
      items: [
        {
          title: "Snapshot antes de mudar",
          text: "As configurações reversíveis são registradas antes da aplicação para permitir rollback.",
        },
        {
          title: "Histórico legível",
          text: "Cada etapa aparece em linguagem clara, com resultado e motivo.",
        },
        {
          title: "Sem scripts remotos",
          text: "A otimização vem dentro do aplicativo; nenhum comando desconhecido é baixado para executar.",
        },
        {
          title: "Proteções preservadas",
          text: "O app não desativa Defender, firewall, UAC, Windows Update ou serviços essenciais.",
        },
      ],
      github: "Audite o projeto no GitHub",
    },
    streamers: {
      eyebrow: "PRONTO PARA QUEM JOGA E TRANSMITE",
      title: "Sua live continua sendo prioridade.",
      body: "Se OBS Studio, Streamlabs ou TikTok LIVE Studio estiver instalado ou aberto, o FiveMCleaner reconhece o ambiente e preserva ferramentas de transmissão. Ele não encerra esses processos nem altera cenas, perfis, gravações ou chats.",
      note: "A proteção para streaming faz parte dos três perfis — não é um modo separado que você precisa lembrar de ativar.",
      platforms: ["OBS Studio", "Streamlabs", "TikTok LIVE Studio"],
      safeTitle: "O que fica protegido",
      safeItems: [
        "Processos de transmissão",
        "Cenas e perfis",
        "Pastas de gravação",
        "Câmera, chat e áudio",
      ],
      honest: "O app não promete aumentar bitrate ou qualidade da live: ele evita que a otimização prejudique seu fluxo de transmissão.",
    },
    requirements: {
      eyebrow: "ANTES DE INSTALAR",
      title: "Tudo o que você precisa já vem junto.",
      body: "O instalador inclui o runtime necessário. Não é preciso procurar .NET, pacotes extras ou executar comandos manualmente.",
      systemTitle: "Requisitos",
      items: [
        "Windows 10 22H2 ou Windows 11",
        "Sistema x64 (64 bits)",
        "Permissão de administrador nas ações de sistema",
        "Conexão apenas para atualizações e relatórios opcionais",
      ],
      installerTitle: "Instalação limpa",
      installerItems: [
        "Runtime incluído no instalador",
        "Atalho e desinstalação integrados",
        "Atualizações via GitHub Releases",
        "Sem download de dependências desconhecidas",
      ],
    },
    safety: {
      eyebrow: "SEGURANÇA SEM MARKETING VAZIO",
      title: "Forte no que faz. Transparente sobre os limites.",
      body: "O FiveMCleaner usa APIs e configurações documentadas do Windows, publica o código-fonte e evita técnicas que costumam gerar bloqueios ou colocar o sistema em risco.",
      cards: [
        ["Código aberto", "Você pode inspecionar exatamente o que o aplicativo faz."],
        ["Sem evasão", "Nada de ofuscação, exclusões no antivírus, injeção ou truques para esconder comportamento."],
        ["Hash verificável", "Confira o SHA-256 publicado na release antes de instalar."],
      ],
      warningTitle: "Sobre o SmartScreen e antivírus",
      warningBody: "Enquanto o executável não possui assinatura paga, o SmartScreen pode exibir um aviso de reputação. Nenhum projeto unsigned pode garantir zero falso positivo em todos os antivírus. Baixe somente da release oficial e confira o hash; o app não pede para desativar sua proteção.",
    },
    faq: {
      eyebrow: "PERGUNTAS FREQUENTES",
      title: "Sem letra miúda.",
      items: [
        [
          "O FiveMCleaner garante mais FPS?",
          "Não existe ganho universal: hardware, servidor, temperatura e outros fatores influenciam o resultado. O app melhora configurações relevantes, mostra uma estimativa contextual e distingue claramente estimativa de medição real.",
        ],
        [
          "Posso desfazer as alterações?",
          "Sim. As ações reversíveis geram snapshots e podem ser restauradas pelo histórico. O resumo final informa o que possui rollback.",
        ],
        [
          "O perfil Agressivo desativa a segurança do Windows?",
          "Não. Mesmo no perfil mais forte, Defender, firewall, UAC, Windows Update, arquivo de paginação e serviços essenciais são preservados.",
        ],
        [
          "Como funcionam as atualizações?",
          "O aplicativo consulta a release pública no GitHub. Quando existe uma versão estável mais recente, ele avisa e pede sua confirmação antes de baixar e instalar.",
        ],
        [
          "O aplicativo coleta meus dados?",
          "Não há telemetria. A otimização acontece localmente. A rede é usada para verificar atualizações e somente envia um relatório de bug se você escolher essa ação.",
        ],
        [
          "Por que o SmartScreen pode alertar?",
          "Aplicativos novos e sem certificado pago ainda não têm reputação suficiente no Windows. Isso não é escondido: use sempre o link oficial e valide o hash SHA-256 da release.",
        ],
      ],
    },
    finalCta: {
      eyebrow: "FIVEMCLEANER",
      title: "Um clique. Um plano claro. Seu PC no controle.",
      body: "Baixe a versão estável mais recente e deixe o aplicativo decidir com segurança o que faz sentido para o seu computador.",
      download: "Baixar instalador",
      github: "Abrir GitHub",
      note: "Grátis · código aberto · Windows x64",
    },
    footer: {
      tagline: "Otimização transparente para FiveM no Windows.",
      product: "Produto",
      trust: "Transparência",
      developed: "Desenvolvido por Felipe Marquezini",
      rights: "© 2026 FiveMCleaner. Todos os direitos reservados.",
      noTracking: "Este site não usa cookies, analytics ou formulários.",
    },
  },
  en: {
    skip: "Skip to content",
    languageLabel: "Select language",
    navLabel: "Main navigation",
    highlightsLabel: "Highlights",
    summaryLabel: "Product summary",
    platformsLabel: "Preserved platforms",
    nav: {
      profiles: "Profiles",
      howItWorks: "How it works",
      safety: "Safety",
      faq: "FAQ",
    },
    headerDownload: "Download",
    hero: {
      eyebrow: "OFFICIAL DOWNLOAD CENTER",
      titleStart: "A better prepared PC.",
      titleAccent: "A smoother FiveM.",
      body: "A premium experience for preparing Windows, FiveM and GTA V Legacy with clear technical decisions, live progress and changes you can review.",
      download: "Download installer",
      github: "View code on GitHub",
      releaseNote: "Download via GitHub Releases · no account required",
      included: "Runtime included",
      windows: "Windows 10 and 11",
      rollback: "Rollback available",
      releaseKicker: "OFFICIAL RELEASE",
      releaseTitle: "Simple installation. No extra dependencies.",
      releaseBody: "The installer includes its required runtime and opens the latest stable version from the project’s official source.",
      releaseLink: "Open secure download",
    },
    preview: {
      label: "INTERFACE PREVIEW",
      appStatus: "System ready",
      title: "Your PC, the right plan",
      subtitle: "Local diagnosis complete",
      ringTop: "READY",
      ringBottom: "to optimize",
      profileLabel: "Selected profile",
      light: "Light",
      medium: "Balanced",
      aggressive: "Aggressive",
      recommended: "RECOMMENDED",
      progressTitle: "Preparing safe optimization",
      progressDetail: "Checking current settings…",
      detectedGpu: "CPU and GPU identified",
      detectedGame: "FiveM detected",
      local: "Local processing",
    },
    quickFacts: [
      ["01 click", "to start"],
      ["03 profiles", "decided by the app"],
      ["Local", "no telemetry"],
      ["Reversible", "with snapshots"],
    ],
    profiles: {
      eyebrow: "THREE PRIORITIES, ONE SIMPLE CHOICE",
      title: "Choose a profile. The app handles the rest.",
      intro: "No dozens of confusing checkboxes. Every profile uses a safe action catalog and only changes what actually needs adjustment.",
      items: [
        {
          number: "01",
          name: "Light",
          summary: "More responsiveness without sacrificing looks.",
          ideal: "Ideal for modern PCs that already run well.",
          bullets: [
            "Preserves visual quality",
            "Fixes essential Windows preferences",
            "Removes only safe temporary files",
          ],
        },
        {
          number: "02",
          name: "Balanced",
          summary: "The best balance between image and response.",
          ideal: "Recommended for most players.",
          bullets: [
            "Balances Windows, FiveM and GTA V",
            "Prioritizes the correct GPU when needed",
            "Keeps critical services and protections",
          ],
          badge: "RECOMMENDED",
        },
        {
          number: "03",
          name: "Aggressive",
          summary: "Maximum priority for performance.",
          ideal: "For entry-level or very limited hardware.",
          bullets: [
            "Reduces more nonessential visual effects",
            "Applies the strongest performance set",
            "Still preserves safety and rollback",
          ],
        },
      ],
      estimateTitle: "An honest estimate, based on your PC",
      estimateBody: "FiveMCleaner uses detected hardware and system state to contextualize each profile's potential. When no real benchmark exists, it says “gain not measured” — it never invents FPS.",
      estimateTag: "NO MADE-UP NUMBERS",
    },
    process: {
      eyebrow: "SIMPLE OUTSIDE, CAREFUL INSIDE",
      title: "You can follow every step.",
      intro: "The progress bar shows the current action, elapsed time and an estimated finish time. If something is already correct, the app confirms it and moves on.",
      steps: [
        {
          number: "01",
          title: "Diagnose",
          text: "Identifies the CPU, GPU, memory, Windows, FiveM and GTA V.",
        },
        {
          number: "02",
          title: "Plan",
          text: "Automatically builds a plan compatible with the selected profile.",
        },
        {
          number: "03",
          title: "Protect and apply",
          text: "Records the previous state and runs only the necessary changes.",
        },
        {
          number: "04",
          title: "Summarize",
          text: "Provides a clear history of what changed, what was skipped and what can be undone.",
        },
      ],
    },
    transparency: {
      eyebrow: "CONTROL WITHOUT COMPLEXITY",
      title: "Nothing hidden behind the button.",
      body: "FiveMCleaner automates technical work without turning Windows into a black box.",
      items: [
        {
          title: "Snapshot before changes",
          text: "Reversible settings are recorded before being applied so they can be rolled back.",
        },
        {
          title: "Readable history",
          text: "Every step appears in clear language, including its result and reason.",
        },
        {
          title: "No remote scripts",
          text: "Optimization ships inside the app; it does not download unknown commands to execute.",
        },
        {
          title: "Protections preserved",
          text: "The app does not disable Defender, firewall, UAC, Windows Update or essential services.",
        },
      ],
      github: "Audit the project on GitHub",
    },
    streamers: {
      eyebrow: "READY FOR PLAYERS WHO STREAM",
      title: "Your broadcast remains a priority.",
      body: "If OBS Studio, Streamlabs or TikTok LIVE Studio is installed or open, FiveMCleaner recognizes the environment and preserves streaming tools. It does not close these processes or alter scenes, profiles, recordings or chats.",
      note: "Streaming protection is part of all three profiles — not a separate mode you must remember to enable.",
      platforms: ["OBS Studio", "Streamlabs", "TikTok LIVE Studio"],
      safeTitle: "What stays protected",
      safeItems: [
        "Broadcast processes",
        "Scenes and profiles",
        "Recording folders",
        "Camera, chat and audio",
      ],
      honest: "The app does not promise better bitrate or stream quality: it prevents optimization from harming your broadcast workflow.",
    },
    requirements: {
      eyebrow: "BEFORE INSTALLING",
      title: "Everything you need is already included.",
      body: "The installer includes the required runtime. You do not need to find .NET, install extra packages or run commands manually.",
      systemTitle: "Requirements",
      items: [
        "Windows 10 22H2 or Windows 11",
        "x64 (64-bit) system",
        "Administrator permission for system actions",
        "Connection only for updates and optional reports",
      ],
      installerTitle: "Clean installation",
      installerItems: [
        "Runtime included in the installer",
        "Integrated shortcut and uninstaller",
        "Updates through GitHub Releases",
        "No unknown dependency downloads",
      ],
    },
    safety: {
      eyebrow: "SECURITY WITHOUT EMPTY MARKETING",
      title: "Powerful where it matters. Clear about limits.",
      body: "FiveMCleaner uses documented Windows APIs and settings, publishes its source code and avoids techniques that commonly cause blocks or put the system at risk.",
      cards: [
        ["Open source", "You can inspect exactly what the application does."],
        ["No evasion", "No obfuscation, antivirus exclusions, injection or tricks to hide behavior."],
        ["Verifiable hash", "Check the SHA-256 published with the release before installing."],
      ],
      warningTitle: "About SmartScreen and antivirus",
      warningBody: "While the executable has no paid code-signing certificate, SmartScreen may show a reputation warning. No unsigned project can guarantee zero false positives across every antivirus. Download only from the official release and verify the hash; the app never asks you to disable protection.",
    },
    faq: {
      eyebrow: "FREQUENTLY ASKED QUESTIONS",
      title: "No fine print.",
      items: [
        [
          "Does FiveMCleaner guarantee more FPS?",
          "There is no universal gain: hardware, server, temperature and other factors affect results. The app improves relevant settings, provides a contextual estimate and clearly separates estimates from real measurements.",
        ],
        [
          "Can I undo the changes?",
          "Yes. Reversible actions create snapshots and can be restored from history. The final summary identifies what supports rollback.",
        ],
        [
          "Does the Aggressive profile disable Windows security?",
          "No. Even in the strongest profile, Defender, firewall, UAC, Windows Update, pagefile and essential services remain protected.",
        ],
        [
          "How do updates work?",
          "The app checks the public GitHub release. When a newer stable version exists, it notifies you and asks for confirmation before downloading and installing.",
        ],
        [
          "Does the application collect my data?",
          "There is no telemetry. Optimization runs locally. The network is used to check for updates and only sends a bug report if you choose that action.",
        ],
        [
          "Why might SmartScreen show a warning?",
          "New apps without a paid certificate do not yet have enough Windows reputation. This is not hidden: always use the official link and validate the release SHA-256 hash.",
        ],
      ],
    },
    finalCta: {
      eyebrow: "FIVEMCLEANER",
      title: "One click. One clear plan. Your PC in control.",
      body: "Download the latest stable version and let the app safely decide what makes sense for your computer.",
      download: "Download installer",
      github: "Open GitHub",
      note: "Free · open source · Windows x64",
    },
    footer: {
      tagline: "Transparent optimization for FiveM on Windows.",
      product: "Product",
      trust: "Transparency",
      developed: "Developed by Felipe Marquezini",
      rights: "© 2026 FiveMCleaner. All rights reserved.",
      noTracking: "This website uses no cookies, analytics or forms.",
    },
  },
} as const;

function CheckMark() {
  return <span className="check-mark" aria-hidden="true">✓</span>;
}

export default function Home() {
  const [language, setLanguage] = useState<Language>("pt");
  const text = copy[language];

  useEffect(() => {
    document.documentElement.lang = language === "pt" ? "pt-BR" : "en";
    document.title = language === "pt"
      ? "FiveMCleaner — Otimização transparente para FiveM"
      : "FiveMCleaner — Transparent optimization for FiveM";
  }, [language]);

  return (
    <div className="site-frame">
      <a className="skip-link" href="#main-content">
        {text.skip}
      </a>

      <header className="site-header">
        <div className="header-inner">
          <a className="brand" href="#top" aria-label="FiveMCleaner">
            <Image src="/icon.png" width={38} height={38} alt="" unoptimized priority />
            <span>FiveM<span>Cleaner</span></span>
          </a>

          <nav className="main-nav" aria-label={text.navLabel}>
            <a href="#profiles">{text.nav.profiles}</a>
            <a href="#how-it-works">{text.nav.howItWorks}</a>
            <a href="#safety">{text.nav.safety}</a>
            <a href="#faq">{text.nav.faq}</a>
          </nav>

          <div className="header-actions">
            <div className="language-switcher" role="group" aria-label={text.languageLabel}>
              <button
                type="button"
                className={language === "pt" ? "active" : ""}
                aria-pressed={language === "pt"}
                onClick={() => setLanguage("pt")}
              >
                PT
              </button>
              <span aria-hidden="true">/</span>
              <button
                type="button"
                className={language === "en" ? "active" : ""}
                aria-pressed={language === "en"}
                onClick={() => setLanguage("en")}
              >
                EN
              </button>
            </div>
            <a className="header-download" href={DOWNLOAD_URL}>
              {text.headerDownload}
            </a>
          </div>
        </div>
      </header>

      <main id="main-content">
        <section className="hero" id="top">
          <div className="ambient ambient-one" aria-hidden="true" />
          <div className="ambient ambient-two" aria-hidden="true" />
          <div className="section-shell hero-grid">
            <div className="hero-copy">
              <p className="eyebrow"><span />{text.hero.eyebrow}</p>
              <h1>
                {text.hero.titleStart}
                <strong>{text.hero.titleAccent}</strong>
              </h1>
              <p className="hero-body">{text.hero.body}</p>

              <div className="hero-actions">
                <a className="button button-primary" href={DOWNLOAD_URL}>
                  <span>{text.hero.download}</span>
                  <span aria-hidden="true">↓</span>
                </a>
                <a className="button button-secondary" href={GITHUB_URL} target="_blank" rel="noreferrer">
                  {text.hero.github}
                  <span aria-hidden="true">↗</span>
                </a>
              </div>
              <p className="release-note"><span aria-hidden="true">●</span>{text.hero.releaseNote}</p>

              <aside className="release-card" aria-label={text.hero.releaseKicker}>
                <div className="release-card-icon" aria-hidden="true">↓</div>
                <div>
                  <span>{text.hero.releaseKicker}</span>
                  <strong>{text.hero.releaseTitle}</strong>
                  <p>{text.hero.releaseBody}</p>
                  <a href={DOWNLOAD_URL}>{text.hero.releaseLink}<b aria-hidden="true">↗</b></a>
                </div>
              </aside>

              <ul className="trust-list" aria-label={text.highlightsLabel}>
                <li><CheckMark />{text.hero.included}</li>
                <li><CheckMark />{text.hero.windows}</li>
                <li><CheckMark />{text.hero.rollback}</li>
              </ul>
            </div>

            <div className="product-preview-wrap">
              <p className="preview-label">{text.preview.label}</p>
              <div className="product-preview">
                <div className="preview-topbar">
                  <div className="preview-brand">
                    <Image src="/icon.png" width={28} height={28} alt="" unoptimized />
                    <span>FiveMCleaner</span>
                  </div>
                  <span className="system-ready"><i />{text.preview.appStatus}</span>
                </div>

                <div className="preview-content">
                  <div className="preview-heading">
                    <div>
                      <h2>{text.preview.title}</h2>
                      <p>{text.preview.subtitle}</p>
                    </div>
                    <div className="readiness-ring" aria-label={`${text.preview.ringTop} ${text.preview.ringBottom}`}>
                      <div>
                        <strong>{text.preview.ringTop}</strong>
                        <small>{text.preview.ringBottom}</small>
                      </div>
                    </div>
                  </div>

                  <div className="profile-selector">
                    <div className="selector-label">{text.preview.profileLabel}</div>
                    <div className="profile-pills">
                      <span>{text.preview.light}</span>
                      <span className="selected">
                        {text.preview.medium}
                        <small>{text.preview.recommended}</small>
                      </span>
                      <span>{text.preview.aggressive}</span>
                    </div>
                  </div>

                  <div className="progress-card">
                    <div className="progress-copy">
                      <div>
                        <strong>{text.preview.progressTitle}</strong>
                        <span>{text.preview.progressDetail}</span>
                      </div>
                      <span>62%</span>
                    </div>
                    <div className="progress-track" aria-hidden="true"><span /></div>
                  </div>

                  <div className="detection-grid">
                    <span><CheckMark />{text.preview.detectedGpu}</span>
                    <span><CheckMark />{text.preview.detectedGame}</span>
                    <span><CheckMark />{text.preview.local}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>

        <div className="section-shell quick-facts" aria-label={text.summaryLabel}>
          {text.quickFacts.map(([value, label]) => (
            <div key={value}>
              <strong>{value}</strong>
              <span>{label}</span>
            </div>
          ))}
        </div>

        <section className="content-section profiles-section" id="profiles">
          <div className="section-shell">
            <div className="section-intro centered">
              <p className="eyebrow"><span />{text.profiles.eyebrow}</p>
              <h2>{text.profiles.title}</h2>
              <p>{text.profiles.intro}</p>
            </div>

            <div className="profile-grid">
              {text.profiles.items.map((profile, index) => (
                <article className={`profile-card ${index === 1 ? "featured" : ""}`} key={profile.number}>
                  <div className="profile-card-top">
                    <span className="profile-number">{profile.number}</span>
                    {"badge" in profile && profile.badge ? <span className="recommended-badge">{profile.badge}</span> : null}
                  </div>
                  <h3>{profile.name}</h3>
                  <p className="profile-summary">{profile.summary}</p>
                  <p className="profile-ideal">{profile.ideal}</p>
                  <ul>
                    {profile.bullets.map((bullet) => <li key={bullet}><CheckMark />{bullet}</li>)}
                  </ul>
                </article>
              ))}
            </div>

            <div className="estimate-panel">
              <div className="estimate-mark" aria-hidden="true">≈</div>
              <div>
                <span className="estimate-tag">{text.profiles.estimateTag}</span>
                <h3>{text.profiles.estimateTitle}</h3>
                <p>{text.profiles.estimateBody}</p>
              </div>
            </div>
          </div>
        </section>

        <section className="content-section process-section" id="how-it-works">
          <div className="section-shell">
            <div className="section-intro split-intro">
              <div>
                <p className="eyebrow"><span />{text.process.eyebrow}</p>
                <h2>{text.process.title}</h2>
              </div>
              <p>{text.process.intro}</p>
            </div>

            <div className="process-grid">
              {text.process.steps.map((step) => (
                <article className="process-step" key={step.number}>
                  <span className="step-number">{step.number}</span>
                  <div className="step-rule" aria-hidden="true" />
                  <h3>{step.title}</h3>
                  <p>{step.text}</p>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section className="content-section transparency-section">
          <div className="section-shell transparency-grid">
            <div className="transparency-copy">
              <p className="eyebrow"><span />{text.transparency.eyebrow}</p>
              <h2>{text.transparency.title}</h2>
              <p>{text.transparency.body}</p>
              <a href={GITHUB_URL} target="_blank" rel="noreferrer">{text.transparency.github}<span aria-hidden="true">↗</span></a>
            </div>
            <div className="transparency-list">
              {text.transparency.items.map((item, index) => (
                <article key={item.title}>
                  <span className="square-check" aria-hidden="true">✓</span>
                  <div>
                    <h3>{item.title}</h3>
                    <p>{item.text}</p>
                  </div>
                  <span className="item-index">0{index + 1}</span>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section className="content-section streamer-section">
          <div className="section-shell streamer-panel">
            <div className="streamer-copy">
              <p className="eyebrow"><span />{text.streamers.eyebrow}</p>
              <h2>{text.streamers.title}</h2>
              <p>{text.streamers.body}</p>
              <p className="streamer-note"><span aria-hidden="true">i</span>{text.streamers.note}</p>
              <div className="platform-list" aria-label={text.platformsLabel}>
                {text.streamers.platforms.map((platform) => <span key={platform}>{platform}</span>)}
              </div>
            </div>

            <div className="streamer-safe-card">
              <div className="live-indicator"><span />SAFE</div>
              <h3>{text.streamers.safeTitle}</h3>
              <ul>
                {text.streamers.safeItems.map((item) => <li key={item}><CheckMark />{item}</li>)}
              </ul>
              <p>{text.streamers.honest}</p>
            </div>
          </div>
        </section>

        <section className="content-section requirements-section">
          <div className="section-shell">
            <div className="section-intro centered narrow">
              <p className="eyebrow"><span />{text.requirements.eyebrow}</p>
              <h2>{text.requirements.title}</h2>
              <p>{text.requirements.body}</p>
            </div>

            <div className="requirements-grid">
              <article>
                <span className="card-kicker">WINDOWS</span>
                <h3>{text.requirements.systemTitle}</h3>
                <ul>
                  {text.requirements.items.map((item) => <li key={item}><CheckMark />{item}</li>)}
                </ul>
              </article>
              <article className="accent-card">
                <span className="card-kicker">SETUP</span>
                <h3>{text.requirements.installerTitle}</h3>
                <ul>
                  {text.requirements.installerItems.map((item) => <li key={item}><CheckMark />{item}</li>)}
                </ul>
              </article>
            </div>
          </div>
        </section>

        <section className="content-section safety-section" id="safety">
          <div className="section-shell">
            <div className="section-intro split-intro">
              <div>
                <p className="eyebrow"><span />{text.safety.eyebrow}</p>
                <h2>{text.safety.title}</h2>
              </div>
              <p>{text.safety.body}</p>
            </div>

            <div className="safety-card-grid">
              {text.safety.cards.map(([title, body], index) => (
                <article key={title}>
                  <span className="safety-icon" aria-hidden="true">{index === 0 ? "{ }" : index === 1 ? "✓" : "#"}</span>
                  <h3>{title}</h3>
                  <p>{body}</p>
                </article>
              ))}
            </div>

            <div className="warning-panel">
              <span className="warning-symbol" aria-hidden="true">!</span>
              <div>
                <h3>{text.safety.warningTitle}</h3>
                <p>{text.safety.warningBody}</p>
              </div>
            </div>
          </div>
        </section>

        <section className="content-section faq-section" id="faq">
          <div className="section-shell faq-layout">
            <div className="faq-heading">
              <p className="eyebrow"><span />{text.faq.eyebrow}</p>
              <h2>{text.faq.title}</h2>
              <Image src="/icon.png" width={108} height={108} alt="" unoptimized />
            </div>
            <div className="faq-list">
              {text.faq.items.map(([question, answer], index) => (
                <details key={question} open={index === 0}>
                  <summary>
                    <span>{question}</span>
                    <span className="faq-plus" aria-hidden="true">+</span>
                  </summary>
                  <p>{answer}</p>
                </details>
              ))}
            </div>
          </div>
        </section>

        <section className="final-cta-section">
          <div className="section-shell final-cta">
            <div className="final-logo" aria-hidden="true">
              <Image src="/icon.png" width={72} height={72} alt="" unoptimized />
            </div>
            <p className="eyebrow"><span />{text.finalCta.eyebrow}</p>
            <h2>{text.finalCta.title}</h2>
            <p>{text.finalCta.body}</p>
            <div className="hero-actions centered-actions">
              <a className="button button-primary" href={DOWNLOAD_URL}>
                <span>{text.finalCta.download}</span><span aria-hidden="true">↓</span>
              </a>
              <a className="button button-secondary" href={GITHUB_URL} target="_blank" rel="noreferrer">
                {text.finalCta.github}<span aria-hidden="true">↗</span>
              </a>
            </div>
            <span className="final-note">{text.finalCta.note}</span>
          </div>
        </section>
      </main>

      <footer className="site-footer">
        <div className="section-shell footer-top">
          <div>
            <a className="brand footer-brand" href="#top" aria-label="FiveMCleaner">
              <Image src="/icon.png" width={42} height={42} alt="" unoptimized />
              <span>FiveM<span>Cleaner</span></span>
            </a>
            <p>{text.footer.tagline}</p>
          </div>
          <div className="footer-links">
            <div>
              <strong>{text.footer.product}</strong>
              <a href="#profiles">{text.nav.profiles}</a>
              <a href="#how-it-works">{text.nav.howItWorks}</a>
              <a href={DOWNLOAD_URL}>{text.headerDownload}</a>
            </div>
            <div>
              <strong>{text.footer.trust}</strong>
              <a href="#safety">{text.nav.safety}</a>
              <a href="#faq">{text.nav.faq}</a>
              <a href={GITHUB_URL}>GitHub</a>
            </div>
          </div>
        </div>
        <div className="section-shell footer-bottom">
          <div>
            <span>{text.footer.developed}</span>
            <span>{text.footer.rights}</span>
          </div>
          <span>{text.footer.noTracking}</span>
        </div>
      </footer>
    </div>
  );
}
