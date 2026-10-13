import { useState } from "react";

// Decides when a field's error may be shown, so people are not scolded before they finish typing.
// An error shows once the field has been left with something typed in it (`touch`), or once the
// form has been submitted (`submit`). The state is temporary and lives only in the form.
export function useTouched<K extends string>() {
  const [submitted, setSubmitted] = useState(false);
  const [touched, setTouched] = useState<Partial<Record<K, boolean>>>({});

  return {
    // Marks the whole form as submitted, so every field may show its error.
    submit: () => setSubmitted(true),
    // Marks one field as visited when it loses focus; ignored while the field is still empty.
    touch: (field: K, value: string) => {
      if (value === "") return;
      setTouched((prev) => (prev[field] ? prev : { ...prev, [field]: true }));
    },
    // True when this field's error is allowed to show.
    shown: (field: K) => submitted || touched[field] === true,
  };
}
