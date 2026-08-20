"use client";

import { useEffect, useState } from "react";
import type { Language } from "./content/copy";
import { copy } from "./content/copy";
import { SiteHeader } from "./components/SiteHeader";
import { HeroSection, QuickFacts } from "./components/HeroSection";
import { ProfilesSection } from "./components/ProfilesSection";
import { ProcessSection } from "./components/ProcessSection";
import { TransparencySection } from "./components/TransparencySection";
import { StreamersSection } from "./components/StreamersSection";
import { RequirementsSection } from "./components/RequirementsSection";
import { SafetySection } from "./components/SafetySection";
import { FaqSection } from "./components/FaqSection";
import { FinalCtaSection } from "./components/FinalCtaSection";
import { SiteFooter } from "./components/SiteFooter";

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

      <SiteHeader language={language} setLanguage={setLanguage} text={text} />

      <main id="main-content">
        <HeroSection text={text} />
        <QuickFacts text={text} />
        <ProfilesSection text={text} />
        <ProcessSection text={text} />
        <TransparencySection text={text} />
        <StreamersSection text={text} />
        <RequirementsSection text={text} />
        <SafetySection text={text} />
        <FaqSection text={text} />
        <FinalCtaSection text={text} />
      </main>

      <SiteFooter text={text} />
    </div>
  );
}
