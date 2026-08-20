import type { Copy } from "../content/copy";
import { CheckMark } from "./Icons";

export function RequirementsSection({ text }: { text: Copy }) {
  return (
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
  );
}
