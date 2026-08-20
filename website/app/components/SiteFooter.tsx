import Image from "next/image";
import type { Copy } from "../content/copy";
import { DISCORD_URL, DOWNLOAD_URL, GITHUB_URL, ICON_URL } from "../content/copy";
import { DiscordIcon } from "./Icons";

export function SiteFooter({ text }: { text: Copy }) {
  return (
      <footer className="site-footer">
        <div className="section-shell footer-top">
          <div>
            <a className="brand footer-brand" href="#top" aria-label="FiveMCleaner">
              <Image src={ICON_URL} width={42} height={42} alt="" unoptimized />
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
            <div>
              <strong>{text.footer.community}</strong>
              <a className="footer-discord" href={DISCORD_URL} target="_blank" rel="noreferrer">
                <DiscordIcon />
                {text.footer.discord}
              </a>
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
  );
}
