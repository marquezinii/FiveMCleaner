import type { Copy } from "../content/copy";

export function ProcessSection({ text }: { text: Copy }) {
  return (
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
  );
}
