import Image from "next/image";
import type { Language } from "../content/copy";
import type { Copy } from "../content/copy";
import { DISCORD_URL, DOWNLOAD_URL, ICON_URL } from "../content/copy";
import { DiscordIcon } from "./Icons";

export function SiteHeader({
  language,
  setLanguage,
  text,
}: {
  language: Language;
  setLanguage: (language: Language) => void;
  text: Copy;
}) {
  return (
      <header className="site-header">
        <div className="header-inner">
          <a className="brand" href="#top" aria-label="FiveMCleaner">
            <Image src={ICON_URL} width={38} height={38} alt="" unoptimized priority />
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
            <a
              className="discord-link"
              href={DISCORD_URL}
              target="_blank"
              rel="noreferrer"
              aria-label={text.discordLabel}
              title={text.discordLabel}
            >
              <DiscordIcon />
            </a>
            <a className="header-download" href={DOWNLOAD_URL}>
              {text.headerDownload}
            </a>
          </div>
        </div>
      </header>
  );
}
