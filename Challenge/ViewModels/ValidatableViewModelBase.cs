using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Challenge.ViewModels
{
    /// <summary>
    /// Adds <see cref="INotifyDataErrorInfo"/> support so WPF bindings can display validation errors next to the inputs.
    /// </summary>
    public abstract class ValidatableViewModelBase : ViewModelBase, INotifyDataErrorInfo
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _errors = new Dictionary<string, IReadOnlyList<string>>();

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public bool HasErrors => _errors.Count > 0;

        /// <summary>All current validation errors, e.g. for a validation summary in the view.</summary>
        public IReadOnlyList<string> AllErrors => _errors.Values.SelectMany(errors => errors).ToList();

        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return AllErrors;
            }

            return _errors.TryGetValue(propertyName, out var propertyErrors) ? propertyErrors : Enumerable.Empty<string>();
        }

        public bool HasErrorsFor(string propertyName) => _errors.ContainsKey(propertyName);

        /// <summary>
        /// Replaces the errors of <paramref name="propertyName"/>; passing <c>null</c> or no errors clears them.
        /// </summary>
        protected void SetErrors(string propertyName, params string[] errors)
        {
            var newErrors = (errors ?? Array.Empty<string>()).Where(error => !string.IsNullOrEmpty(error)).ToList();
            _errors.TryGetValue(propertyName, out var currentErrors);

            if (newErrors.SequenceEqual(currentErrors ?? Enumerable.Empty<string>()))
            {
                return;
            }

            if (newErrors.Count == 0)
            {
                _errors.Remove(propertyName);
            }
            else
            {
                _errors[propertyName] = newErrors;
            }

            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(AllErrors));
        }
    }
}
