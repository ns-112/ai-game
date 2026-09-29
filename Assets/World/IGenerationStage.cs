using System;

// Implemented by any AI generator whose full completion (including whatever
// texture calls it kicks off, not just its own initial request) something
// else — like WorldLoadingGate — might want to wait on.
public interface IGenerationStage
{
    event Action OnFinished;
}
