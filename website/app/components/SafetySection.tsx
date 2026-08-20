import type { Copy } from "../content/copy";

export function SafetySection({ text }: { text: Copy }) {
  return (
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
  );
}
