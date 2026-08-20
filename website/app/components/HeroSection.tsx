import Image from "next/image";
import type { Copy } from "../content/copy";
import { DOWNLOAD_URL, GITHUB_URL, ICON_URL } from "../content/copy";
import { CheckMark } from "./Icons";

export function HeroSection({ text }: { text: Copy }) {
  return (
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
                    <Image src={ICON_URL} width={28} height={28} alt="" unoptimized />
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
  );
}

export function QuickFacts({ text }: { text: Copy }) {
  return (
        <div className="section-shell quick-facts" aria-label={text.summaryLabel}>
          {text.quickFacts.map(([value, label]) => (
            <div key={value}>
              <strong>{value}</strong>
              <span>{label}</span>
            </div>
          ))}
        </div>
  );
}
