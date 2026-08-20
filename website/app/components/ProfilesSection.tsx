import type { Copy } from "../content/copy";
import { CheckMark } from "./Icons";

export function ProfilesSection({ text }: { text: Copy }) {
  return (
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
  );
}
