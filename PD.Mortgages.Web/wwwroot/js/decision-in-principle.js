(() => {
    const assessment = document.getElementById("dip-assessment");
    const continueButton = document.getElementById("continue-to-mortgages");
    const token = document.querySelector("#dip-antiforgery input[name='__RequestVerificationToken']");

    if (!assessment || !continueButton || !token || assessment.dataset.needsAssessment !== "true") {
        return;
    }

    const requestAssessment = async () => {
        const loadingMessage = assessment.dataset.creditCheckConsented === "true"
            ? "Running a simulated credit check…"
            : "Preparing your indicative guide…";
        assessment.innerHTML = `
            <div class="dip-loading" role="status" aria-live="polite">
                <span class="spinner-border text-primary" aria-hidden="true"></span>
                <div>
                    <strong>${loadingMessage}</strong>
                    <p class="mb-0">This usually takes a few seconds. No credit agency or lender is contacted.</p>
                </div>
            </div>`;
        continueButton.disabled = true;

        try {
            const response = await fetch(assessment.dataset.assessmentUrl, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "RequestVerificationToken": token.value,
                    "X-Requested-With": "XMLHttpRequest"
                }
            });

            if (!response.ok) {
                throw new Error(`Assessment request failed with status ${response.status}.`);
            }

            assessment.innerHTML = await response.text();
            assessment.dataset.needsAssessment = "false";
            continueButton.disabled = false;
        } catch {
            assessment.replaceChildren();

            const error = document.createElement("div");
            error.className = "dip-request-error";
            error.setAttribute("role", "alert");

            const message = document.createElement("p");
            message.textContent = "We couldn’t complete the assessment just now. Please try again.";
            error.append(message);

            const retry = document.createElement("button");
            retry.className = "btn btn-outline-primary";
            retry.type = "button";
            retry.textContent = "Try again";
            retry.addEventListener("click", requestAssessment, { once: true });
            error.append(retry);

            assessment.append(error);
        }
    };

    requestAssessment();
})();
