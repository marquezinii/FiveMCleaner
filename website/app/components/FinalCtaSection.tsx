import Image from "next/image";
import type { Copy } from "../content/copy";
import { DOWNLOAD_URL, GITHUB_URL, ICON_URL } from "../content/copy";

export function FinalCtaSection({ text }: { text: Copy }) {
  return (
        <section className="final-cta-section">
          <div className="section-shell final-cta">
            <div className="final-logo" aria-hidden="true">
              <Image src={ICON_URL} width={72} height={72} alt="" unoptimized />
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
  );
}
